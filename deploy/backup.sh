#!/usr/bin/env bash
# Backs up the database and the uploaded files into ./backups, and keeps the last 14 days. Run nightly from cron on
# the server (docs/server-setup.md), then copy ./backups somewhere else: a backup on the same disk is lost with it.
#   ./backup.sh
set -euo pipefail
cd "$(dirname "$0")"

set -a
. ./.env
. ./deployed.env
set +a

stamp=$(date -u +%Y%m%d-%H%M%S)
mkdir -p backups
chmod 700 backups

# The password goes through the environment, not the command line.
docker compose -f docker-compose.prod.yml exec -T -e MYSQL_PWD="$MYSQL_ROOT_PASSWORD" db \
    mysqldump -uroot --single-transaction --routines --triggers --set-gtid-purged=OFF "$MYSQL_DATABASE" \
    | gzip > "backups/db-$stamp.sql.gz"

# The container runs as root so it can read every uploaded file, then gives the backups back to this script's user:
# otherwise the chmod below fails on them and old backups are never deleted.
docker run --rm -v id-indetails_files:/files:ro -v "$PWD/backups:/out" alpine:3 \
    sh -c 'tar -czf "/out/files-$1.tar.gz" -C /files . && chown -R "$2" /out' sh "$stamp" "$(id -u):$(id -g)"

chmod 600 backups/*
find backups -type f -mtime +14 -delete
echo "Backed up to backups/db-$stamp.sql.gz and backups/files-$stamp.tar.gz"
