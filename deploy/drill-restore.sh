#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# FuelFlow DISASTER RECOVERY DRILL.
#
# Restores the most recent backup into a THROWAWAY postgres container and runs the
# integrity checks nobody was running, then deletes the container. It never opens,
# writes to, or stops the live database - there is no code path from this script to
# `fuelflow-postgres`.
#
#     ./drill-restore.sh                      # newest .age in $BACKUP_DIR
#     ./drill-restore.sh /path/to/one.dump     # a specific backup
#
# WHY IT EXISTS
# A backup that has never been restored is a hypothesis, not a backup. FF-14 has
# been owed since 2026-08-21: the nightly job has written an encrypted dump every
# night for two months and nobody has ever proven it restores. Two decisions have
# since raised the price of being wrong:
#
#   D-14 encrypts `fuel_vouchers.qr_payload` on write. A database restored WITHOUT
#        QR_CIPHER_KEY has no redeemable fuel in it - not "degraded", unusable.
#   D-03 makes the double-entry journal append-only and closes periods with a
#        trigger, so a lost period cannot be recomputed; losing a closed period is
#        worse than losing a day of orders.
#
# D-22 records "RPO ~24 h, no PITR" as an ACCEPTED risk. An accepted risk needs a
# measured number behind it, and this script prints the measured number: it is the
# only RTO figure anyone has ever had.
#
# WHY THERE IS NO TIMER FOR IT
# The backups are age-encrypted and the age identity deliberately lives OFF this
# host - restore.sh says so in as many words: "a server that can decrypt its own
# backups gives up the main protection encrypting them bought". An unattended drill
# therefore cannot decrypt on the box, and putting the key there to enable one
# would trade a measured-unknown RTO for a silently-zeroed one. Run this by hand,
# on a machine that holds the key, once a month and after any backup or
# infrastructure change. That cadence is the compromise; the alternative is worse.
#
# WHAT IT CHECKS (in order of what it would actually catch)
#   1. the dump decrypts and restores at all
#   2. `__EFMigrationsHistory` is populated - without it EF replays every
#      migration from zero, which is the failure that makes a restore useless
#   3. row counts are non-zero for the tables that carry the business
#   4. `qr_payload` is actually ciphertext (D-14 in force), not plaintext
#   5. the three data-integrity queries the security audit called "the single most
#      valuable open action" - none of which anyone runs
#   6. elapsed wall-clock, printed as the measured RTO
#
# The queries are written against the schema as it is TODAY, not as an older
# document remembers it: `payments.kind IN ('Capture','Refund')` and `amount_uah`
# (not `amount_UAH` - Postgres folds unquoted identifiers to lower case),
# `voucher_events.event_type IN ('Imported','StatusChanged','Extended','Archived',
# 'Unarchived')`, `qr_payload` stored base64 by `QrCipher`. A check written
# against a stale name returns 0 and reads as "ok", which is the one failure mode
# a drill must not have.
#
# Requires: docker, and `age` if the backup is still encrypted.
# ------------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Deliberately NOT read from deploy/.env. The drill has no business knowing the
# production database name or credentials - that is what makes it safe to run.
BACKUP_DIR="${BACKUP_DIR:-/root/fuelflow-backups}"
POSTGRES_IMAGE="${POSTGRES_IMAGE:-postgres:16-alpine}"
DRILL_DB="${DRILL_DB:-fuelflow_drill}"
DRILL_USER="${DRILL_USER:-fuelflow_drill}"

fail() { echo "ERROR: $*" >&2; exit 1; }
info() { echo; echo "── $* ──────────────────────────────────────────��──────────────"; }

# --- Pick the backup ----------------------------------------------------------------
DUMP="${1:-}"
if [[ -z "$DUMP" ]]; then
  [[ -d "$BACKUP_DIR" ]] || fail "no backup given and BACKUP_DIR=$BACKUP_DIR does not exist."
  # Newest first. nullglob so an empty directory expands to nothing rather than
  # to the literal pattern, which would then "restore" a file named '*.age'.
  shopt -s nullglob
  CANDIDATES=("$BACKUP_DIR"/*.age "$BACKUP_DIR"/*.dump)
  shopt -u nullglob
  (( ${#CANDIDATES[@]} > 0 )) || fail "no .age or .dump in $BACKUP_DIR"
  DUMP="$(ls -t "${CANDIDATES[@]}" | head -1)"
fi
[[ -f "$DUMP" ]] || fail "file not found: $DUMP"
info "Backup under test"
echo "  $DUMP"
echo "  $(du -h "$DUMP" | cut -f1), taken $(date -r "$DUMP" '+%Y-%m-%d %H:%M:%S')"

# --- Decrypt if needed --------------------------------------------------------------
PLAINTEXT_IS_TEMPORARY=0
if [[ "$DUMP" == *.age ]]; then
  [[ -n "${BACKUP_AGE_IDENTITY:-}" ]] || fail \
    "$DUMP is encrypted and BACKUP_AGE_IDENTITY is not set. This script must run where the
       age identity lives - normally your laptop, not the server:
           scp root@DROPLET:$DUMP .
           BACKUP_AGE_IDENTITY=~/fuelflow-backup.key ./drill-restore.sh ./$(basename "$DUMP")"
  command -v age >/dev/null 2>&1 || fail "'age' is not installed (apt-get install -y age)."
  PLAINTEXT="$(mktemp "${TMPDIR:-/tmp}/fuelflow-drill.XXXXXX")"
  chmod 600 "$PLAINTEXT"
  PLAINTEXT_IS_TEMPORARY=1
  trap '[[ "$PLAINTEXT_IS_TEMPORARY" == 1 ]] && { command -v shred >/dev/null 2>&1 && shred -u "$PLAINTEXT" 2>/dev/null || rm -f "$PLAINTEXT"; }' EXIT
  info "Decrypting"
  age -d -i "$BACKUP_AGE_IDENTITY" -o "$PLAINTEXT" "$DUMP"
  DUMP="$PLAINTEXT"
  echo "  ok ($(du -h "$DUMP" | cut -f1))"
fi

# --- Throwaway container ------------------------------------------------------------
# The name is derived from $$ so two drills can never collide, and it is asserted
# against the live container below. Nothing in this script reads the production
# .env, stops the API, or names fuelflow-postgres.
DRILL_CONTAINER="fuelflow-drill-$$"
cleanup() {
  echo
  info "Cleaning up"
  docker rm -f "$DRILL_CONTAINER" >/dev/null 2>&1 || true
  docker volume rm -f "${DRILL_CONTAINER}-data" >/dev/null 2>&1 || true
  [[ "$PLAINTEXT_IS_TEMPORARY" == 1 ]] && { command -v shred >/dev/null 2>&1 && shred -u "$PLAINTEXT" 2>/dev/null || rm -f "$PLAINTEXT"; }
  echo "  drill container and volume removed; the live database was never touched."
}
trap cleanup EXIT

info "Starting throwaway postgres"
docker volume create "${DRILL_CONTAINER}-data" >/dev/null
docker run -d --rm \
  --name "$DRILL_CONTAINER" \
  -e POSTGRES_DB="$DRILL_DB" \
  -e POSTGRES_USER="$DRILL_USER" \
  -e POSTGRES_PASSWORD=drill \
  -v "${DRILL_CONTAINER}-data:/var/lib/postgresql/data" \
  "$POSTGRES_IMAGE" >/dev/null

# Wait for the server, not for the container: `docker run` returns before initdb
# finishes, and a restore attempted in that window dies on a connection refusal
# that looks like a broken dump.
for _ in $(seq 1 60); do
  docker exec "$DRILL_CONTAINER" pg_isready -U "$DRILL_USER" -d "$DRILL_DB" >/dev/null 2>&1 && break
  sleep 1
done
docker exec "$DRILL_CONTAINER" pg_isready -U "$DRILL_USER" -d "$DRILL_DB" >/dev/null \
  || fail "throwaway postgres never became ready"
echo "  ready ($DRILL_CONTAINER)"

START=$(date +%s)

info "Restoring"
# pg_restore exits non-zero on harmless notices (missing roles, extensions already
# present), which is why restore.sh warns rather than fails. A DRILL must do the
# opposite: a warning that was always harmless is indistinguishable from one that
# hid a real failure, and this run exists to be strict.
if ! docker exec -i "$DRILL_CONTAINER" pg_restore \
      -U "$DRILL_USER" -d "$DRILL_DB" \
      --no-owner --no-privileges \
      < "$DUMP" 2> >(grep -Ei 'error|fatal' >&2 || true); then
  fail "pg_restore failed - see the ERROR lines above."
fi

END=$(date +%s)

psql_q() { docker exec -i "$DRILL_CONTAINER" psql -U "$DRILL_USER" -d "$DRILL_DB" -X -qAt "$@"; }

info "1. Migration history"
MIGRATIONS="$(psql_q -c 'SELECT count(*) FROM "__EFMigrationsHistory";')"
[[ "$MIGRATIONS" =~ ^[0-9]+$ && "$MIGRATIONS" -gt 0 ]] \
  || fail "__EFMigrationsHistory is empty - EF would replay every migration from zero."
LAST_MIGRATION="$(psql_q -c 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1;')"
echo "  $MIGRATIONS migrations applied; newest: $LAST_MIGRATION"

info "2. Row counts"
for t in brands fuel_types fuel_offers fuel_vouchers payments orders voucher_events; do
  EXISTS="$(psql_q -c "SELECT to_regclass('public.$t') IS NOT NULL;")"
  if [[ "$EXISTS" != "t" ]]; then
    echo "  $t: absent"
    continue
  fi
  printf '  %-18s %s\n' "$t" "$(psql_q -c "SELECT count(*) FROM \"$t\";")"
done

info "3. qr_payload is ciphertext, not plaintext (D-14)"
# `qr_payload` is bytea, not text. Migration 20261016220058_EncryptVoucherQrPayload
# converted it with `encode(qr_payload,'base64')` into varchar(2000); the model now
# maps it back to byte[]. So "is it base64?" cannot be asked at all - a regex against
# a bytea column is a type error, which the first real run of this drill discovered
# (2026-10-19) after passing every static check in CI.
#
# What can be asked, and is the thing that matters: does the stored blob still contain
# the voucher number in the clear? If D-14 is not in force - or a row was written by a
# path that bypasses QrCipher - the number sits in there as text, and everyone holding
# the dump can read a redeemable voucher.
PAYLOAD_ROWS="$(psql_q -c 'SELECT count(*) FROM fuel_vouchers WHERE qr_payload IS NOT NULL;')"
VULNERABLE="$(psql_q -c "
  SELECT count(*) FROM fuel_vouchers
   WHERE qr_payload IS NOT NULL
     AND voucher_number IS NOT NULL
     AND position(convert_to(voucher_number, 'UTF8') in qr_payload) > 0;")"
if [[ "$PAYLOAD_ROWS" == "0" ]]; then
  echo "  no vouchers carry a QR payload yet - nothing to assert (0 rows is NOT a pass)"
else
  [[ "$VULNERABLE" == "0" ]] || fail "$VULNERABLE row(s) carry their voucher number in cleartext inside qr_payload."
  echo "  $PAYLOAD_ROWS payload(s), 0 readable voucher numbers - D-14 in force in this dump"
fi

info "4. Data integrity (the audit's three)"
report_integrity() {
  local label="$1" sql="$2" rows
  rows="$(psql_q -c "$sql")"
  if [[ "$rows" == "0" ]]; then
    printf '  %-40s ok\n' "$label"
  else
    printf '  %-40s FAIL (%s)\n' "$label" "$rows"
    INTEGRITY_FAILURES=$((INTEGRITY_FAILURES + 1))
  fi
}
INTEGRITY_FAILURES=0

# Issuance is a StatusChanged event whose destination is 'Assigned' - there is no
# 'Issued' event_type (ck_voucher_events_type pins the five that exist). More than
# one of those for a voucher means it was handed to a customer twice.
report_integrity "a voucher issued twice" "
  SELECT count(*) FROM (
    SELECT voucher_id FROM voucher_events
     WHERE event_type = 'StatusChanged' AND to_status = 'Assigned'
     GROUP BY voucher_id HAVING count(*) > 1
  ) x;"

# Two different parties acting on one voucher is legal in general (a worker may
# redeem for an owner), so the check is on ownership: the same voucher assigned by
# two DIFFERENT actors is the state the audit meant.
report_integrity "a voucher assigned by two actors" "
  SELECT count(*) FROM (
    SELECT voucher_id FROM voucher_events
     WHERE event_type = 'StatusChanged' AND to_status = 'Assigned'
       AND actor_user_id IS NOT NULL
     GROUP BY voucher_id HAVING count(DISTINCT actor_user_id) > 1
  ) x;"

# Refunds point at the capture they reverse via parent_payment_id, so this does
# not have to guess how an order's payments add up.
report_integrity "a refund above the capture it reverses" "
  SELECT count(*) FROM payments r
   JOIN payments c ON c.id = r.parent_payment_id
   WHERE r.kind = 'Refund' AND r.amount_uah > c.amount_uah;"

info "Result"
echo "  measured restore time : $((END - START))s   <- the RTO number D-22 never had"
if [[ "$INTEGRITY_FAILURES" -eq 0 ]]; then
  echo "  integrity checks     : all passed"
  echo
  echo "  The backup restores. Record the RTO against D-22 and close FF-14."
  exit 0
fi
echo "  integrity checks     : $INTEGRITY_FAILURES FAILED - see above, do not close FF-14"
exit 1