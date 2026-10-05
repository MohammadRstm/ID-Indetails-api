#!/usr/bin/env bash
# How many test users to seed for a profile: USERS when set, otherwise the profile's peak of virtual users (at least
# 60), plus a few for those who leave during the seeded history. run.sh seeds this many, and the Load test workflow
# names its seed cache after it.
#
#   load-tests/scripts/seed-users.sh <profile>
set -euo pipefail

profile=${1:?Usage: seed-users.sh <profile>}
if [ -n "${USERS:-}" ]; then
    echo "$USERS"
    exit 0
fi

here=$(cd "$(dirname "$0")/.." && pwd)
needed=$(jq '[.steps[].users] | max' "$here/k6/profiles/$profile.json" 2> /dev/null || echo 0)
[ "$profile" != concurrency ] || needed=200
[ "$needed" -ge 60 ] || needed=60
echo $((needed + needed / 20 + 5))
