#!/usr/bin/env bash
# Runs inside the db container, from deploy.sh: gives the API its own MySQL user with data rights only, so the
# running API can read and write rows but never change or drop the schema. Migrations keep the schema owner
# (MYSQL_USER). Safe to run on every deployment: it also applies a changed MYSQL_API_PASSWORD.
#   docker compose -f docker-compose.prod.yml exec -T db bash -s < api-db-user.sh
set -euo pipefail

: "${MYSQL_API_USER:?}" "${MYSQL_API_PASSWORD:?}" "${MYSQL_DATABASE:?}"
[[ "$MYSQL_API_USER" =~ ^[A-Za-z0-9_]+$ && "$MYSQL_API_PASSWORD" =~ ^[A-Za-z0-9]+$ && "$MYSQL_DATABASE" =~ ^[A-Za-z0-9_]+$ ]] \
  || { echo "MYSQL_API_USER and MYSQL_DATABASE: letters, digits, _; MYSQL_API_PASSWORD: letters and digits." >&2; exit 1; }
[[ "$MYSQL_API_USER" != "root" && "$MYSQL_API_USER" != "${MYSQL_USER:-}" ]] \
  || { echo "MYSQL_API_USER must be its own user, not root or MYSQL_USER." >&2; exit 1; }

MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql --user=root --silent <<SQL
CREATE USER IF NOT EXISTS '${MYSQL_API_USER}'@'%' IDENTIFIED BY '${MYSQL_API_PASSWORD}';
ALTER USER '${MYSQL_API_USER}'@'%' IDENTIFIED BY '${MYSQL_API_PASSWORD}';
GRANT SELECT, INSERT, UPDATE, DELETE ON \`${MYSQL_DATABASE}\`.* TO '${MYSQL_API_USER}'@'%';
SQL
