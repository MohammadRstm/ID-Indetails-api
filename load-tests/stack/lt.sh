#!/usr/bin/env bash
# Runs the load-test environment (docker-compose.loadtest.yml) on the server, from its own folder, ~/idi-loadtest.
# Every command first checks that it is in the load-test folder with the load-test .env; `check` and `up` also check
# the rendered compose configuration (Mailpit only, internal API network, project-owned volumes) before anything
# starts, and `up` then checks the running API can reach nothing but Mailpit. See load-tests/README.md.
#
#   ./lt.sh init-env <public-ip> <api-tag> <client-tag>   write .env with fresh random secrets (once)
#   ./lt.sh check [--email-test] [--production-rate-limits]
#   ./lt.sh up    [--email-test] [--production-rate-limits] [service...]
#   ./lt.sh verify-email      the running API sends to Mailpit and can't reach anything outside
#   ./lt.sh mail-count        how many emails Mailpit has caught
#   ./lt.sh status | logs [service...] | restart <service...>
#   ./lt.sh down              remove the containers, keep the data
#   ./lt.sh purge             remove the containers and every load-test volume (database, uploads, certificates)
set -euo pipefail

readonly PROJECT=idi-loadtest
readonly SERVICES=(lt-db lt-mail lt-api lt-web lt-proxy)
here=$(cd "$(dirname "$0")" && pwd)
cd "$here"

# Nothing from the caller's shell may point compose at another project or file.
unset COMPOSE_PROJECT_NAME COMPOSE_FILE COMPOSE_PROFILES COMPOSE_ENV_FILES

fail() {
    echo "REFUSED: $*" >&2
    exit 1
}

env_value() {
    grep -m1 "^$1=" .env | cut -d= -f2- || true
}

require_loadtest_folder() {
    [ -f docker-compose.loadtest.yml ] || fail "docker-compose.loadtest.yml isn't in $here."
    [ ! -e docker-compose.prod.yml ] || fail "$here holds production's docker-compose.prod.yml. lt.sh runs from its own folder, ~/idi-loadtest."
    [ "$here" != /opt/id-indetails ] || fail "$here is production's folder."
}

require_env_file() {
    [ -f .env ] || fail "There is no .env here. Write one with: ./lt.sh init-env <public-ip> <api-tag> <client-tag>"
    if [ "$(uname -s)" = Linux ] && [ "$(stat -c %a .env)" != 600 ]; then
        fail ".env must be readable by its owner only: chmod 600 .env"
    fi
    # Production's names. Their presence means someone copied production's .env here.
    if grep -Eq '^[[:space:]]*(SMTP_|MYSQL_|JWT_SIGNING_KEY|SETUP_SUPER_ADMIN_KEY|PUBLIC_DOMAIN|ACME_EMAIL|Email__|ConnectionStrings__)' .env; then
        fail ".env holds production setting names. It must be the load-test file (.env.loadtest.example), never a copy of production's."
    fi
    grep -qx 'LT_ENVIRONMENT=loadtest' .env || fail ".env doesn't say LT_ENVIRONMENT=loadtest."

    local name
    for name in LT_INSTANCE_ID LT_PUBLIC_HOST LT_GATE_KEY LT_DB_ROOT_PASSWORD LT_DB_APP_PASSWORD LT_JWT_SIGNING_KEY API_TAG CLIENT_TAG; do
        [ -n "$(env_value "$name")" ] || fail "$name is empty in .env."
    done
    [[ "$(env_value LT_INSTANCE_ID)" =~ ^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$ ]] || fail "LT_INSTANCE_ID isn't a GUID."
    [[ "$(env_value LT_GATE_KEY)" =~ ^[0-9a-f]{64}$ ]] || fail "LT_GATE_KEY must be 64 hex characters."
    [ "$(env_value LT_JWT_SIGNING_KEY | wc -c)" -gt 32 ] || fail "LT_JWT_SIGNING_KEY must be at least 32 characters."
}

require_tools() {
    command -v docker >/dev/null || fail "docker isn't installed."
    command -v python3 >/dev/null || fail "python3 isn't installed (it checks the compose configuration)."
}

require_all() {
    require_loadtest_folder
    require_tools
    require_env_file
}

# Options that pick the opt-in compose files; whatever is left are service names.
email_test=false
compose_files=(-f docker-compose.loadtest.yml)
services=()
parse_options() {
    local arg
    for arg in "$@"; do
        case "$arg" in
            --email-test)
                email_test=true
                compose_files+=(-f docker-compose.loadtest.email-test.yml)
                ;;
            --production-rate-limits)
                compose_files+=(-f docker-compose.loadtest.production-limits.yml)
                ;;
            -*) fail "Unknown option $arg." ;;
            *)
                [[ " ${SERVICES[*]} " == *" $arg "* ]] || fail "No service $arg. The services are: ${SERVICES[*]}"
                services+=("$arg")
                ;;
        esac
    done
}

compose() {
    docker compose -p "$PROJECT" --project-directory "$here" --env-file "$here/.env" "${compose_files[@]}" "$@"
}

CONFIG_CHECK=$(cat <<'PY'
import json, sys

config = json.load(sys.stdin)
email_test = sys.argv[1] == "true"
problems = []

def need(ok, message):
    if not ok:
        problems.append(message)

services = config.get("services") or {}
networks = config.get("networks") or {}
need(config.get("name") == "idi-loadtest", "the compose project isn't idi-loadtest")
need(sorted(services) == ["lt-api", "lt-db", "lt-mail", "lt-proxy", "lt-web"], f"unexpected services {sorted(services)}")

api = services.get("lt-api") or {}
env = {key: "" if value is None else str(value) for key, value in (api.get("environment") or {}).items()}
need(env.get("Email__Host") == "lt-mail", "the API's Email__Host isn't lt-mail (Mailpit)")
need(env.get("Email__Port") == "1025", "the API's Email__Port isn't Mailpit's 1025")
need(env.get("Email__Username", "") == "" and env.get("Email__Password", "") == "", "the API has SMTP credentials")
need(env.get("Email__FromAddress", "").endswith("@loadtest.invalid"), "the API's Email__FromAddress isn't @loadtest.invalid")
connection = env.get("ConnectionStrings__Default", "")
need("Server=lt-db;" in connection and "Database=idi_loadtest;" in connection, "the API's database isn't idi_loadtest on lt-db")
need(env.get("NotificationScheduler__Enabled") == ("true" if email_test else "false"),
     "the notification scheduler must be off, except with --email-test")
need(set(api.get("networks") or {}) == {"lt-backend"}, "the API isn't on lt-backend alone")
need(set((services.get("lt-mail") or {}).get("networks") or {}) == {"lt-backend"}, "Mailpit isn't on lt-backend alone")
need((networks.get("lt-backend") or {}).get("internal") is True, "lt-backend isn't an internal network")
need((services.get("lt-db") or {}).get("hostname") == "idi-loadtest-db", "the database's hostname isn't idi-loadtest-db")

published = sorted(
    (name, port.get("host_ip", ""), str(port.get("published")), int(port.get("target")))
    for name, service in services.items()
    for port in service.get("ports") or [])
need(published == [("lt-db", "127.0.0.1", "13306", 3306), ("lt-proxy", "", "8443", 8443)], f"unexpected published ports {published}")

for kind, items in (("volume", config.get("volumes") or {}), ("network", networks)):
    for key, item in items.items():
        need(not item.get("external") and str(item.get("name", "")).startswith("idi-loadtest_"),
             f"{kind} {key} isn't the load-test project's own")

if problems:
    print("\n".join("REFUSED: compose configuration: " + p for p in problems), file=sys.stderr)
    sys.exit(1)
print("Config     ok: mail to Mailpit only, API on an internal network, scheduler " + ("on (email test)" if email_test else "off"))
PY
)

check_rendered_config() {
    compose config --format json | python3 -c "$CONFIG_CHECK" "$email_test"
}

ENV_CHECK=$(cat <<'PY'
import json, sys

env = dict(item.split("=", 1) for item in json.load(sys.stdin))
problems = []
if env.get("Email__Host") != "lt-mail" or env.get("Email__Port") != "1025":
    problems.append("its SMTP server isn't lt-mail:1025")
if env.get("Email__Username") or env.get("Email__Password"):
    problems.append("it has SMTP credentials")
if not env.get("Email__FromAddress", "").endswith("@loadtest.invalid"):
    problems.append("its From address isn't @loadtest.invalid")
if any(key.startswith("SMTP_") for key in env):
    problems.append("it has production's SMTP_* variables")
if problems:
    print("\n".join("REFUSED: running API: " + p for p in problems), file=sys.stderr)
    sys.exit(1)
print("API mail   lt-mail:1025, no credentials, scheduler " + ("on" if env.get("NotificationScheduler__Enabled") == "true" else "off"))
PY
)

verify_email() {
    local id banner
    id=$(compose ps -q lt-api)
    [ -n "$id" ] || fail "lt-api isn't running."

    docker inspect --format '{{json .Config.Env}}' "$id" | python3 -c "$ENV_CHECK"

    # The egress probe passes when its command fails, so first make sure commands run in the container at all.
    compose exec -T lt-api true || fail "can't run commands in lt-api, so its network can't be checked."
    if compose exec -T lt-api timeout 5 bash -c 'exec 3<>/dev/tcp/1.1.1.1/443' 2>/dev/null; then
        fail "the API reached 1.1.1.1:443, so it could reach a real mail server. lt-backend must be internal."
    fi
    echo "Egress     none: the API can't open connections outside the server"

    banner=$(compose exec -T lt-api timeout 5 bash -c 'exec 3<>/dev/tcp/lt-mail/1025 && head -n1 <&3') \
        || fail "the API can't reach Mailpit at lt-mail:1025."
    [[ "$banner" == *Mailpit* ]] || fail "lt-mail:1025 doesn't answer as Mailpit: $banner"
    echo "Mailpit    answers at lt-mail:1025"
}

mail_count() {
    local response
    response=$(compose exec -T lt-api timeout 5 bash -c \
        'exec 3<>/dev/tcp/lt-mail/8025 && printf "GET /api/v1/messages?limit=1 HTTP/1.0\r\nHost: lt-mail\r\n\r\n" >&3 && cat <&3') \
        || fail "Mailpit didn't answer."
    python3 -c 'import json, sys; print(json.loads(sys.stdin.read().split("\r\n\r\n", 1)[1])["total"])' <<< "$response"
}

init_env() {
    require_loadtest_folder
    [ $# -eq 3 ] || fail "Usage: ./lt.sh init-env <public-ip-or-name> <api-tag> <client-tag>"
    [ ! -e .env ] || fail ".env already exists. New secrets need a purge first; delete .env yourself if that is what you want."
    [[ "$1" =~ ^[A-Za-z0-9.-]+$ ]] || fail "Not an IP address or host name: $1"
    local tag
    for tag in "$2" "$3"; do
        [[ "$tag" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$ ]] || fail "Not an image tag: $tag"
    done

    # Hex, so no secret ever needs escaping in a connection string.
    (
        umask 077
        cat > .env <<EOF
LT_ENVIRONMENT=loadtest
LT_INSTANCE_ID=$(cat /proc/sys/kernel/random/uuid)
LT_PUBLIC_HOST=$1
LT_GATE_KEY=$(openssl rand -hex 32)
LT_DB_ROOT_PASSWORD=$(openssl rand -hex 24)
LT_DB_APP_PASSWORD=$(openssl rand -hex 24)
LT_JWT_SIGNING_KEY=$(openssl rand -hex 32)
API_TAG=$2
CLIENT_TAG=$3
EOF
    )
    chmod 600 .env
    echo "Wrote $here/.env with new random secrets."
}

command=${1:-}
[ $# -gt 0 ] && shift
case "$command" in
    init-env)
        init_env "$@"
        ;;
    check)
        require_all
        parse_options "$@"
        [ ${#services[@]} -eq 0 ] || fail "check takes no service names."
        check_rendered_config
        ;;
    up)
        require_all
        parse_options "$@"
        check_rendered_config
        compose up -d --wait --wait-timeout 180 "${services[@]}"
        if [ -n "$(compose ps -q lt-api)" ]; then
            verify_email
        fi
        ;;
    verify-email)
        require_all
        verify_email
        ;;
    mail-count)
        require_all
        mail_count
        ;;
    status)
        require_all
        compose ps
        ;;
    logs)
        require_all
        parse_options "$@"
        compose logs --tail 200 "${services[@]}"
        ;;
    restart)
        require_all
        parse_options "$@"
        [ ${#services[@]} -gt 0 ] || fail "Name the services to restart: ${SERVICES[*]}"
        compose restart "${services[@]}"
        ;;
    down)
        require_all
        compose down --remove-orphans
        ;;
    purge)
        require_all
        while read -r volume; do
            [[ "$volume" == "${PROJECT}_"* ]] || fail "Volume $volume carries the $PROJECT label but not its name. Nothing removed."
        done < <(docker volume ls -q --filter "label=com.docker.compose.project=$PROJECT")
        compose down -v --remove-orphans
        echo "Removed the load-test containers, networks and volumes."
        ;;
    *)
        sed -n '2,14p' "$0" >&2
        exit 2
        ;;
esac
