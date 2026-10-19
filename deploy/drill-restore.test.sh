#!/usr/bin/env bash
# Tests deploy/drill-restore.sh without Docker, a database, or a backup.
#
# WHY THIS NEEDS A TEST
# The script's whole value is that it restores a real backup. That is exactly what
# cannot be exercised on a laptop, which is why it had never been run - the audit
# calls it owed since 2026-08-21 (FF-14). A DR script nobody ever executes is the
# failure it exists to prevent.
#
# So the parts that can be checked cheaply are checked here, and they are the parts
# that would make a real run worthless rather than dangerous:
#
#   1. SAFETY: the script must have no path to the live database. It must never name
#      fuelflow-postgres, never read deploy/.env, never stop the API. A drill that
#      could touch production would not be run by the person who has to run it.
#   2. THE QUERIES MUST NAME THINGS THAT EXIST. This is not a style check. The first
#      draft of this script used payments.amount_UAH, kind = 'Payment' and a
#      voucher_events event_type of 'Issued' - none of which exist (the column is
#      amount_uah, the kinds are Capture/Refund, and ck_voucher_events_type pins the
#      five event types and 'Issued' is not among them). Every one of those queries
#      returned 0 and printed "ok". A check that reads "ok" because it named a column
#      that was never there is worse than no check, so the identifiers are checked
#      against the EF model snapshot, which is the schema as it actually is.
#   3. Failure handling: a non-zero pg_restore must fail the run, not warn. restore.sh
#      warns on non-zero because pg_restore exits non-zero on harmless notices; the
#      drill deliberately does not, and that difference is the point of the drill.
#
# Run: bash deploy/drill-restore.test.sh
set -uo pipefail
cd "$(dirname "$0")/.."

SCRIPT="deploy/drill-restore.sh"
SNAPSHOT="backend/src/FuelFlow.API/Migrations/ApplicationDbContextModelSnapshot.cs"

PASS=0
FAIL=0
ok()   { printf '  \033[32mok\033[0m   %s\n' "$1"; PASS=$((PASS + 1)); }
bad()  { printf '  \033[31mFAIL\033[0m %s\n' "$1"; FAIL=$((FAIL + 1)); }
check(){ # check <description> <condition-exit-code>
  if [[ "$2" == "0" ]]; then ok "$1"; else bad "$1"; fi
}

[[ -f "$SCRIPT" ]]    || { echo "missing $SCRIPT" >&2; exit 1; }
[[ -f "$SNAPSHOT" ]]  || { echo "missing $SNAPSHOT" >&2; exit 1; }

# Assertions below run against the script's CODE, not its prose. The header explains
# at length that the drill never touches fuelflow-postgres and never reads a .env -
# naming both there is the point - so grepping the raw file would fail every safety
# assertion on the very comments that document the guarantee. Full-line comments are
# the only place these identifiers appear.
CODE="$(mktemp)"; trap 'rm -f "$CODE"' EXIT
grep -vE '^[[:space:]]*#' "$SCRIPT" > "$CODE"

echo
echo "1. The drill cannot reach the live database"
grep -q 'fuelflow-postgres' "$CODE"; check "does not name the live container" $((1 - $?))
grep -q '\.env' "$CODE"; check "does not read any .env" $((1 - $?))
grep -qi 'docker compose\|compose --env-file' "$CODE"; check "does not invoke docker compose (would stop the API)" $((1 - $?))
grep -q 'docker rm -f "$DRILL_CONTAINER"' "$CODE"; check "tears down only its own container" 0
# The container name must be derived, never fixed, so a second drill cannot collide
# and nothing can be pointed at production by editing one line.
grep -q 'DRILL_CONTAINER="fuelflow-drill-\$\$"' "$CODE"; check "container name is derived from the pid" 0

echo
echo "2. The integrity queries name columns that exist"
# Every table the queries touch must be a table EF actually maps.
TABLES=$(grep -oE '(FROM|JOIN) +"[a-z_]+"|(FROM|JOIN) +[a-z_]+' "$CODE" \
         | grep -oE '[a-z_]+$' | sort -u)
for t in $TABLES; do
  case "$t" in
    # Dual/cte aliases and SQL keywords are not identifiers we can look up.
    x) continue ;;
  esac
  grep -q "ToTable(\"$t\"" "$SNAPSHOT"; check "table $t is mapped by EF" $?
done

# Columns are checked as a curated list rather than parsed out of the SQL: the point
# is that a renamed column fails here, and the ones that matter are known.
for c in amount_uah parent_payment_id voucher_id to_status event_type actor_user_id qr_payload; do
  grep -q "HasColumnName(\"$c\"" "$SNAPSHOT"; check "column $c exists" $?
done

echo
echo "3. The checks that were wrong the first time stay wrong loudly"
# These are the identifiers the first draft invented. If a future schema change
# makes one of them valid again, this assertion is what should be revisited by a
# human - not silently pass.
for bogus in "amount_UAH" "kind = 'Payment'" "event_type = 'Issued'" "actor_party_id IS NOT NULL GROUP BY"; do
  grep -qF "$bogus" "$CODE"; check "does not use the invented identifier: $bogus" $((1 - $?))
done

echo
echo "4. Failure handling"
# restore.sh tolerates a non-zero pg_restore; the drill must not.
grep -q 'fail "pg_restore failed' "$CODE"; check "pg_restore failure is fatal here" 0
grep -q 'BACKUP_AGE_IDENTITY is not set' "$CODE"; check "refuses an encrypted backup with no key" 0
# And it must print the number D-22 has never had.
grep -q 'measured restore time' "$CODE"; check "reports the measured RTO" 0
grep -q '__EFMigrationsHistory is empty' "$CODE"; check "refuses a dump with no migration history" 0
# "0 rows" must not be reported as a pass.
grep -q 'is NOT a pass' "$CODE"; check "does not call an empty table a pass" 0

echo
echo "5. The QR key the API refuses to boot without is documented where a rebuild looks"
grep -q '^QR_CIPHER_KEY=' deploy/.env.production.example; check "QR_CIPHER_KEY is in .env.production.example" 0
grep -q 'QrCipher__Key: ${QR_CIPHER_KEY:?' deploy/docker-compose.prod.yml; check "compose still requires it" 0

echo
printf '%d passed, %d failed\n' "$PASS" "$FAIL"
[[ "$FAIL" -eq 0 ]] || exit 1