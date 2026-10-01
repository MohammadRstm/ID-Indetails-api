#!/usr/bin/env bash
# Runs on the server, from the Deploy workflow: pulls the chosen images, migrates the database, then replaces the
# API and the client and waits until both are healthy. Safe to run again with the same tags.
#   API_TAG=sha-abc1234 CLIENT_TAG=sha-def5678 ./deploy.sh
set -euo pipefail
cd "$(dirname "$0")"

: "${API_TAG:?}" "${CLIENT_TAG:?}"
export API_TAG CLIENT_TAG
compose=(docker compose -f docker-compose.prod.yml)

echo "Deploying API ${API_TAG}, client ${CLIENT_TAG}"
"${compose[@]}" pull db api web caddy
"${compose[@]}" --profile migrate pull migrations
"${compose[@]}" up -d --wait db
"${compose[@]}" --profile migrate run --rm migrations
"${compose[@]}" up -d --wait --wait-timeout 180 api web
"${compose[@]}" up -d caddy
"${compose[@]}" ps
# What runs now, for commands run by hand on the server (docs/server-setup.md) and backup.sh.
printf 'API_TAG=%s
CLIENT_TAG=%s
' "$API_TAG" "$CLIENT_TAG" > deployed.env
echo "Deployed API ${API_TAG}, client ${CLIENT_TAG}"
