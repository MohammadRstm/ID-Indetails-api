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

docker run --rm -v id-indetails_files:/files:ro -v "$PWD/backups:/out" alpine:3 \
    tar -czf "/out/files-$stamp.tar.gz" -C /files .

chmod 600 backups/*
find backups -type f -mtime +14 -delete
echo "Backed up to backups/db-$stamp.sql.gz and backups/files-$stamp.tar.gz"
