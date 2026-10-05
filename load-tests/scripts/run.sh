#!/usr/bin/env bash
# One load test, from start to finish, run from a Linux machine with Docker, .NET 10 and k6 (the Load test workflow
# runs it on a GitHub runner). Read load-tests/README.md first.
#
#   TARGET=runner  everything on this machine: the load-test environment in Docker here, as a rehearsal
#   TARGET=server  the load-test environment on the production server, over SSH (LT_SSH=user@host, LT_SSH_KEY=file)
#
#   PROFILE        smoke | load | stages | stress | spike | soak | breakpoint | recovery | concurrency | rate-limits | email
#   USERS          test users to seed (default: what the profile needs, at least 60)
#   MONTHS         months of seeded history (default 3)
#   KEEP=true      leave the load-test environment running afterwards (default: removed with its data)
#   SEED_CACHE     a folder for the seed: reused from there when it holds one of the same size, saved there when made
#                  (the Load test workflow keeps it in the Actions cache for the week)
#
# Steps: check the target → seed on this machine into a throwaway MySQL, or reuse the cached seed → copy the snapshot
# → start the load-test database and load it → start the environment (it checks email isolation) → smoke test → monitoring → k6 → collect
# logs and samples → verify the database → report → clean up. Clean-up runs whatever happens before it.
set -euo pipefail

here=$(cd "$(dirname "$0")/.." && pwd)
repo=$(cd "$here/.." && pwd)
: "${TARGET:?Set TARGET=runner or TARGET=server}" "${PROFILE:?Set PROFILE}"
[[ "$TARGET" =~ ^(runner|server)$ ]] || { echo "TARGET is runner or server." >&2; exit 2; }
[[ "$PROFILE" =~ ^(smoke|load|stages|stress|spike|soak|breakpoint|recovery|concurrency|rate-limits|email)$ ]] || { echo "Unknown PROFILE $PROFILE." >&2; exit 2; }

RUN_ID=${RUN_ID:-$(date -u +%Y%m%d-%H%M%S)-$PROFILE}
OUT=${OUT:-$here/reports/$RUN_ID}
MONTHS=${MONTHS:-3}
LT_DIR=${LT_DIR:-idi-loadtest}
TOOL="$here/tool/bin/Release/net10.0/ID-Indetails.LoadTests.Tool.dll"
mkdir -p "$OUT"
log() { echo "== $(date -u +%H:%M:%S) $*"; }
mask() { [ -z "${GITHUB_ACTIONS:-}" ] || echo "::add-mask::$1"; }

# The roster must hold at least as many active people as the profile's virtual users.
USERS=$(bash "$here/scripts/seed-users.sh" "$PROFILE")

# ---- The target: lt.sh there, and the tunnel to its database ---------------------------------------------------------

if [ "$TARGET" = server ]; then
    : "${LT_SSH:?Set LT_SSH=user@host}" "${LT_SSH_KEY:?Set LT_SSH_KEY to the SSH key file}"
    ssh_opts=(-i "$LT_SSH_KEY" -o BatchMode=yes -o StrictHostKeyChecking=yes -o ServerAliveInterval=30)
    lt() { ssh "${ssh_opts[@]}" "$LT_SSH" "cd ~/$LT_DIR && ./lt.sh $(printf '%q ' "$@")"; }
    fetch() { scp "${ssh_opts[@]}" "$LT_SSH:$LT_DIR/$1" "$2"; }
    put() { scp "${ssh_opts[@]}" "$1" "$LT_SSH:$LT_DIR/$2"; }
    env_of() { ssh "${ssh_opts[@]}" "$LT_SSH" "grep -m1 '^$1=' ~/$LT_DIR/.env | cut -d= -f2-"; }
else
    LT_DIR="$(mktemp -d)/idi-loadtest"
    mkdir -p "$LT_DIR"
    cp "$here"/stack/{docker-compose.loadtest.yml,docker-compose.loadtest.email-test.yml,docker-compose.loadtest.production-limits.yml,Caddyfile.loadtest,lt.sh,monitor.sh} "$LT_DIR/"
    chmod +x "$LT_DIR/lt.sh" "$LT_DIR/monitor.sh"
    lt() { (cd "$LT_DIR" && ./lt.sh "$@"); }
    fetch() { cp "$LT_DIR/$1" "$2"; }
    put() { cp "$1" "$LT_DIR/$2"; }
    env_of() { grep -m1 "^$1=" "$LT_DIR/.env" | cut -d= -f2-; }
fi

tunnel_pid=""
seed_db=""
monitoring=false
started=false
cleanup() {
    local status=$?
    log "Cleaning up"
    if $monitoring; then lt monitor stop > /dev/null 2>&1 || true; fi
    # A failed run keeps what the environment logged, for finding out why.
    if [ "$status" -ne 0 ] && $started && [ ! -d "$OUT/collected" ]; then
        lt collect collected.tar.gz > /dev/null 2>&1 && fetch collected.tar.gz "$OUT/collected.tar.gz" > /dev/null 2>&1             && mkdir -p "$OUT/collected" && tar -xzf "$OUT/collected.tar.gz" -C "$OUT/collected" && rm -f "$OUT/collected.tar.gz" || true
    fi
    [ -z "$tunnel_pid" ] || kill "$tunnel_pid" 2>/dev/null || true
    [ -z "$seed_db" ] || docker rm -f "$seed_db" > /dev/null 2>&1 || true
    if [ "${KEEP:-false}" = true ]; then
        log "KEEP=true: the load-test environment stays up"
    else
        lt purge > /dev/null 2>&1 || true
        log "The load-test environment and its data are removed"
    fi
    exit $status
}
trap cleanup EXIT

log "Run $RUN_ID: $PROFILE on the $TARGET, $USERS users, $MONTHS months of history"
if [ "$TARGET" = runner ]; then
    lt init-env 127.0.0.1 "${API_TAG:-rehearsal}" "${CLIENT_TAG:-rehearsal}"
else
    for file in docker-compose.loadtest.yml docker-compose.loadtest.email-test.yml docker-compose.loadtest.production-limits.yml Caddyfile.loadtest lt.sh monitor.sh; do
        put "$here/stack/$file" "$file"
    done
    ssh "${ssh_opts[@]}" "$LT_SSH" "chmod +x ~/$LT_DIR/lt.sh ~/$LT_DIR/monitor.sh"
    # The images under test, when given: the tags go into the load-test .env (image-tag characters only).
    for pair in "API_TAG=${API_TAG:-}" "CLIENT_TAG=${CLIENT_TAG:-}"; do
        name=${pair%%=*} tag=${pair#*=}
        [ -n "$tag" ] || continue
        [[ "$tag" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$ ]] || { echo "Not an image tag: $tag" >&2; exit 2; }
        ssh "${ssh_opts[@]}" "$LT_SSH" "sed -i 's/^$name=.*/$name=$tag/' ~/$LT_DIR/.env"
    done
fi
lt check

GATE_KEY=$(env_of LT_GATE_KEY); mask "$GATE_KEY"
DB_ROOT=$(env_of LT_DB_ROOT_PASSWORD); mask "$DB_ROOT"
INSTANCE=$(env_of LT_INSTANCE_ID)
HOST=$(env_of LT_PUBLIC_HOST)
BASE_URL="https://$HOST:8443"

# ---- The seed: reused from SEED_CACHE when it holds one of this size, otherwise made here in a throwaway MySQL ------

log "Building the tool"
dotnet build "$here/tool" -c Release --nologo -v q > /dev/null
seed_size="users=$USERS months=$MONTHS"
if [ -n "${SEED_CACHE:-}" ] && [ "$(cat "$SEED_CACHE/size" 2> /dev/null)" = "$seed_size" ]; then
    log "Reusing the cached seed ($seed_size); its log is seed.log"
    cp "$SEED_CACHE/snapshot.sql.gz" "$SEED_CACHE/roster.json" "$SEED_CACHE/seed.log" "$OUT/"
    USER_PASSWORD=$(cat "$SEED_CACHE/user-password"); mask "$USER_PASSWORD"
else
    log "Seeding $USERS users"
    USER_PASSWORD=$(openssl rand -hex 16); mask "$USER_PASSWORD"
    seed_db="lt-seed-$RUN_ID"
    seed_root=$(openssl rand -hex 16); mask "$seed_root"
    docker run -d --name "$seed_db" --hostname idi-loadtest-seed -p 127.0.0.1:13307:3306 -e MYSQL_ROOT_PASSWORD="$seed_root" mysql:8.4 \
        --character-set-server=utf8mb4 --collation-server=utf8mb4_0900_ai_ci --default-time-zone=+00:00 > /dev/null
    # Over TCP: while it first initialises, MySQL runs a temporary server that answers on its socket only.
    for _ in $(seq 1 90); do docker exec "$seed_db" mysqladmin ping -h 127.0.0.1 -uroot -p"$seed_root" --silent 2> /dev/null && break; sleep 2; done
    seed_tool() {
        LT_DB_CONNECTION="Server=127.0.0.1;Port=13307;User=root;Password=$seed_root;Database=idi_loadtest;" LT_DB_HOSTNAME=idi-loadtest-seed \
            LT_INSTANCE_ID=00000000-0000-4000-8000-000000000001 LT_USER_PASSWORD="$USER_PASSWORD" dotnet "$TOOL" "$@"
    }
    seed_tool init > /dev/null
    seed_tool migrate > /dev/null
    seed_tool seed --users "$USERS" --months "$MONTHS" --roster "$OUT/roster.json" > "$OUT/seed.log" 2>&1 || { tail -20 "$OUT/seed.log"; exit 1; }
    grep -E "^(Seeded|BROKEN|MISMATCH)" "$OUT/seed.log" || true
    docker exec -e MYSQL_PWD="$seed_root" "$seed_db" mysqldump -uroot --single-transaction --routines --triggers --set-gtid-purged=OFF \
        --no-tablespaces idi_loadtest | gzip > "$OUT/snapshot.sql.gz"
    docker rm -f "$seed_db" > /dev/null && seed_db=""

    if [ -n "${SEED_CACHE:-}" ]; then
        # With the password its test users sign in with; the size goes last, so a half-written seed is never reused.
        mkdir -p "$SEED_CACHE"
        cp "$OUT/snapshot.sql.gz" "$OUT/roster.json" "$OUT/seed.log" "$SEED_CACHE/"
        (umask 077 && echo "$USER_PASSWORD" > "$SEED_CACHE/user-password")
        echo "$seed_size" > "$SEED_CACHE/size"
    fi
fi

# ---- The load-test environment ---------------------------------------------------------------------------------------

log "Starting the load-test database and loading the snapshot"
started=true
lt up lt-db
if [ "$TARGET" = server ]; then
    ssh "${ssh_opts[@]}" -o ExitOnForwardFailure=yes -N -L 13306:127.0.0.1:13306 "$LT_SSH" &
    tunnel_pid=$!
    sleep 3
fi
lt_tool() {
    LT_DB_CONNECTION="Server=127.0.0.1;Port=13306;User=root;Password=$DB_ROOT;Database=idi_loadtest;" LT_DB_HOSTNAME=idi-loadtest-db \
        LT_INSTANCE_ID="$INSTANCE" dotnet "$TOOL" "$@"
}
lt_tool init
put "$OUT/snapshot.sql.gz" snapshot.sql.gz
lt load-snapshot snapshot.sql.gz
lt_tool check

options=()
[ "$PROFILE" != email ] || options+=(--email-test)
[ "$PROFILE" != rate-limits ] || options+=(--production-rate-limits)
log "Starting the environment ${options[*]}"
lt up "${options[@]}"

k6_env=(-e "BASE_URL=$BASE_URL" -e "GATE_KEY=$GATE_KEY" -e "USER_PASSWORD=$USER_PASSWORD" -e "ROSTER=$OUT/roster.json"
    -e "OUT=$OUT" -e "COMPANY_UTC_OFFSET=$(TZ=Asia/Beirut date +%:z)")
# Only the email test may call the endpoints that send email (k6/lib/api.js refuses them otherwise).
[ "$PROFILE" != email ] || k6_env+=(-e EMAIL_TEST=true)

if [[ "$PROFILE" =~ ^(load|stages|stress|spike|soak|breakpoint|recovery)$ ]]; then
    log "Smoke test before the real one"
    mkdir -p "$OUT/smoke"
    k6 run --quiet "${k6_env[@]}" -e PROFILE=smoke -e "RUN_ID=$RUN_ID-smoke" -e "OUT=$OUT/smoke" \
        --console-output "$OUT/smoke-console.log" --log-format raw "$here/k6/main.js" || { echo "The smoke test failed: no load test." >&2; exit 1; }
fi

lt monitor start
monitoring=true

if [ "$PROFILE" = recovery ]; then
    (
        sleep 300 && lt chaos restart-api
        sleep 240 && lt chaos kill-api
        sleep 240 && lt chaos pause-db 30
        sleep 210 && lt chaos restart-proxy
    ) > "$OUT/chaos.out" 2>&1 &
fi

log "k6: $PROFILE"
script=main.js
case "$PROFILE" in
    concurrency) script=concurrency.js ;;
    rate-limits) script=rate-limits.js ;;
    email) script=email.js ;;
esac
k6_status=0
k6 run "${k6_env[@]}" -e "PROFILE=$PROFILE" -e "RUN_ID=$RUN_ID" --console-output "$OUT/k6-console.log" --log-format raw \
    "$here/k6/$script" || k6_status=$?
wait || true

log "Collecting logs and samples"
[ "$PROFILE" != email ] || sleep 15  # the reset email goes out in the background
lt monitor stop
monitoring=false
lt collect collected.tar.gz
fetch collected.tar.gz "$OUT/collected.tar.gz"
mkdir -p "$OUT/collected" && tar -xzf "$OUT/collected.tar.gz" -C "$OUT/collected" && rm "$OUT/collected.tar.gz"

# Email: the email test must find every email it sent in Mailpit; any other test must find none, since it calls no
# endpoint that sends email and runs no scheduler. One there means an email path ran that shouldn't have.
caught=$(tr -dc '0-9' < "$OUT/collected/mail-count.txt")
if [ "$PROFILE" = email ]; then
    expected=$(grep -so 'EXPECTED_EMAILS [0-9]*' "$OUT/k6-console.log" | awk '{print $2}' | tail -1 || true)
    log "Mailpit caught ${caught:-?} emails; the test sent ${expected:-?}"
    [ "${caught:-0}" -ge "${expected:-1}" ] || { echo "Mailpit caught fewer emails than the test sent." >&2; k6_status=1; }
elif [ "${caught:-0}" != 0 ]; then
    echo "Mailpit caught $caught emails during a test that sends none: see collected/lt-mail.log and the API log." >&2
    k6_status=1
fi

log "Verifying the database"
verify_status=0
lt_tool verify --log "$OUT/k6-console.log" --run "$RUN_ID" --out "$OUT" > /dev/null || verify_status=$?

if [ -f "$OUT/k6-summary.json" ]; then
    dotnet "$TOOL" report --dir "$OUT" > /dev/null
    log "Report: $OUT/report.md"
fi
rm -f "$OUT/snapshot.sql.gz"

log "k6 exit $k6_status, verification exit $verify_status"
[ "$k6_status" -eq 0 ] && [ "$verify_status" -eq 0 ]
