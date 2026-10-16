#!/usr/bin/env bash
# Which live devices would the app-version floor strand?
#
# `AppVersion:MinimumVersion` is a hard wall, not a nag: a device below it renders the update screen
# instead of the app, and cannot be used its way out. On 2026-10-12 the floor sat at 1.1.0 while the
# stores still served 1.0.1, so every user was sent to a store that could not give them the version
# they needed. The app was unusable, and could not be used its way out.
#
# Run this BEFORE raising the floor, passing the version you are about to set:
#
#   scripts/check-app-version-floor.sh 1.2.0
#
# Read-only. Exits 1 and lists the affected devices when the new floor would strand somebody, 0 when
# it would not, and 2 when the check could not run at all - which is deliberately not the same as 0.
#
# WHY A SCRIPT AND NOT A PREFLIGHT CHECK. It belongs in deploy/preflight-db/ if it could be
# automatic, and it cannot be: the floor is configuration (AppVersion:MinimumVersion in
# appsettings.json), not a row in app_settings, so there is nothing in the database for a preflight
# query to compare the fleet against. A preflight would have to hardcode the floor and be wrong the
# moment it moved - the same class of bug this script exists to catch.
#
# WHAT IT IS FOR, SPECIFICALLY GOOGLE PLAY. Apple publishes a free lookup endpoint reporting the live
# version, so iOS can be checked against the store itself:
#
#   curl -s https://itunes.apple.com/lookup?id=6759175079 | grep -o '"version":"[^"]*"'
#
# Play has no equivalent, and Play Console needs credentials this box does not hold. A store nothing
# has been published to has no users and therefore cannot strand anybody - but the moment an OLDER
# Android build is published it acquires users who are instantly behind the wall, and nothing else in
# the repository would notice. That is what this asks.

set -euo pipefail

FLOOR="${1:-}"
if [[ -z "$FLOOR" ]]; then
  echo "usage: $(basename "$0") <version>   e.g. $(basename "$0") 1.2.0" >&2
  exit 64
fi

CONTAINER="${FUELFLOW_DB_CONTAINER:-fuelflow-postgres}"
DB_USER="${POSTGRES_USER:-fuelflow}"
DB_NAME="${POSTGRES_DB:-fuelflow}"

# Read by STDIN, not by -c: psql interpolates :'floor' when it reads a file or stdin and does not
# when the statement arrives via -c. The first version passed the floor in with -c, psql answered
# `syntax error at or near ":"`, and - because the error had gone to /dev/null and the row filter
# then found nothing - the script printed "no live device is behind <floor>".
#
# That is the silence this repository keeps warning about, written by someone quoting its own warning.
# So the failure is not swallowed below, and an empty answer is only ever read as an empty answer
# after psql has demonstrably succeeded.
#
# Compared as int arrays, not as text: 1.10.0 is above 1.9.0 as versions and below it as strings, and
# a floor is written in semver order by a human who expects that order.
read -r -d '' QUERY <<'SQL' || true
WITH floor AS (
  SELECT string_to_array(:'floor', '.')::int[] AS parts
)
SELECT d.os_version, d.app_version, d.device_model, count(*)
FROM devices d, floor f
WHERE d.app_version IS NOT NULL
  AND d.status <> 'Revoked'
  AND string_to_array(d.app_version, '.')::int[] < f.parts
GROUP BY d.os_version, d.app_version, d.device_model
ORDER BY d.os_version, d.app_version, d.device_model;
SQL

if ! RESULT="$(printf '%s\n' "$QUERY" \
  | docker exec -i "$CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -X -t -A -F'|' \
      -v "floor=$FLOOR" -v ON_ERROR_STOP=1 -f - 2>&1)"; then
  echo "  THE CHECK DID NOT RUN - nothing is known about the fleet:" >&2
  printf '%s\n' "$RESULT" | sed 's/^/    /' >&2
  exit 2
fi

# Filter on the field separator, not on the first character. This query opens no transaction, so
# -t -A emits no psql chatter to filter and the filter is pure loss: an earlier version matched
# ^[a-zA-Z] to strip chatter, and every real row begins with a digit - 26.2|1.0.1|... - so all of
# them were discarded and the script reported "no live device is behind <floor>" while four devices
# sat behind it. A pipe is what only a data row has.
STRANDED="$(printf '%s\n' "$RESULT" | grep -F '|' || true)"

echo "AppVersion:MinimumVersion -> ${FLOOR}"
echo

if [[ -z "$STRANDED" ]]; then
  echo "  no live device is behind ${FLOOR} - raising the floor strands nobody"
  exit 0
fi

echo "  THESE DEVICES WOULD BE STRANDED (platform | installed | model | count):"
printf '%s\n' "$STRANDED" | while IFS='|' read -r platform installed model count; do
  [[ -z "$installed" ]] && continue
  echo "    ${platform} | ${installed} | ${model} | ${count}"
done
echo
echo "  Each renders the update screen instead of the app, and the store they are sent to must serve"
echo "  ${FLOOR} for them to get through. Check that first:"
echo "    curl -s https://itunes.apple.com/lookup?id=6759175079 | grep -o '\"version\":\"[^\"]*\"'"
echo
exit 1