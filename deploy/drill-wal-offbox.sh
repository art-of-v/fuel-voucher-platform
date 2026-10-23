#!/usr/bin/env bash
# Drill the host-loss restore: prove that a database can be rebuilt from the OFF-BOX base
# backup and the OFF-BOX per-segment WAL alone, after the source host is gone.
#
# WHY THIS IS A DRILL AND NOT A UNIT TEST
# The thing being proven is not "the script parses". It is that a real PostgreSQL writes
# a WAL segment, the shipper encrypts and uploads it, and restore-pitr.sh rebuilds a
# cluster from the remote copy after the source has been stopped - and that the row
# written AFTER the base backup is present in the result. A mocked rclone cannot show
# that, because the failure mode is in the bytes and the sequence, not in the control flow.
#
# WHAT IT DOES
#   1. starts a throwaway postgres that archives to a temp directory;
#   2. writes marker 1, switches WAL, waits for the segment to be archived;
#   3. takes a base backup, encrypts it, and puts it in a local "remote";
#   4. writes marker 2 (AFTER the base), switches WAL, waits for that segment;
#   5. runs ship-wal-offbox.sh against the local "remote" (real age, real rclone);
#   6. STOPS the source - this is the host loss;
#   7. runs restore-pitr.sh with --base-remote/--wal-remote only: no path on the source
#      is read;
#   8. asserts marker 2 is in the restored database, and prints the measured ship lag and
#      the recovered point.
#
# Needs: docker, age, age-keygen, rclone (the local backend; no configured remote). The
# postgres image is the same one production uses. It never touches the live database and
# never writes to a real bucket.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PG_IMAGE="${PG_IMAGE:-postgres:16-alpine}"
SRC="${SRC:-fuelflow-wal-drill-src}"
DST="${DST:-fuelflow-wal-drill-dst}"
PORT="${PORT:-55498}"
RESTORE_DATA_DIR=""

TMP="$(mktemp -d "${TMPDIR:-/tmp}/wal-drill.XXXXXX")"
chmod 755 "$TMP"
mkdir -p "$TMP/archive" "$TMP/remote" "$TMP/state"
# The container's postgres user writes the archive; the bind mount is host-owned. This is
# a throwaway directory of test data, so world-writable is fine here.
chmod 777 "$TMP/archive"

cleanup() {
  docker rm -f "$SRC" "$DST" >/dev/null 2>&1 || true
  # restore-pitr.sh --keep leaves its data directory behind. Its files were chowned to
  # the container's postgres user, so remove the CONTENTS from a container, then remove
  # the (now empty) directory from the host.
  if [[ -n "$RESTORE_DATA_DIR" && -d "$RESTORE_DATA_DIR" ]]; then
    docker run --rm -v "$RESTORE_DATA_DIR:/cleanup" --entrypoint sh "$PG_IMAGE" \
      -c 'rm -rf /cleanup/* 2>/dev/null || true' >/dev/null 2>&1 || true
    rmdir "$RESTORE_DATA_DIR" 2>/dev/null || true
    rm -rf "${RESTORE_DATA_DIR%/data}" 2>/dev/null || true
  fi
  rm -rf "$TMP"
}
trap cleanup EXIT

fail() { echo "ERROR: $*" >&2; exit 1; }

command -v docker >/dev/null 2>&1 || fail "docker is required."
command -v age >/dev/null 2>&1 || fail "age is required (apt-get install -y age)."
command -v age-keygen >/dev/null 2>&1 || fail "age-keygen is required."
command -v rclone >/dev/null 2>&1 || fail "rclone is required (apt-get install -y rclone)."

for c in "$SRC" "$DST"; do
  docker rm -f "$c" >/dev/null 2>&1 || true
done

# The key lives with the restore, not with the source. The recipient is the only thing
# the source (and the shipper) ever sees - the same split as production.
age-keygen -o "$TMP/key.txt" >/dev/null 2>&1
RECIPIENT="$(sed -n 's/^# public key: //p' "$TMP/key.txt")"
[[ -n "$RECIPIENT" ]] || fail "could not read the generated age public key."

cat > "$TMP/env" <<EOF
BACKUP_REMOTE=$TMP/remote
BACKUP_AGE_RECIPIENT=$RECIPIENT
EOF

echo "[$(date -Is)] Starting the throwaway source database ..."
docker run -d --name "$SRC" \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_DB=fuelflow \
  -v "$TMP/archive:/var/lib/postgresql/wal" \
  "$PG_IMAGE" \
  postgres -c archive_mode=on -c archive_timeout=5 \
    -c "archive_command=cp %p /var/lib/postgresql/wal/%f" \
  >/dev/null

for _ in $(seq 1 60); do
  if docker exec "$SRC" psql -U postgres -d fuelflow -Atc 'SELECT 1' >/dev/null 2>&1; then break; fi
  sleep 1
done
docker exec "$SRC" psql -U postgres -d fuelflow -Atc 'SELECT 1' >/dev/null 2>&1 || fail "the source database did not start."

wait_for_segment() {
  local segment="$1" deadline=$((SECONDS + 120))
  while (( SECONDS < deadline )); do
    [[ -f "$TMP/archive/$segment" ]] && return 0
    sleep 1
  done
  return 1
}

echo "[$(date -Is)] Writing marker 1 and forcing a WAL switch ..."
docker exec "$SRC" psql -U postgres -d fuelflow -v ON_ERROR_STOP=1 -q \
  -c "CREATE TABLE drill_marker (id integer PRIMARY KEY, note text NOT NULL, at timestamptz NOT NULL DEFAULT clock_timestamp());" \
  -c "INSERT INTO drill_marker (id, note) VALUES (1, 'before-base');"
# The segment that holds marker 1 is the one being written now; capture it BEFORE the
# switch, then switch, then wait for that exact file in the archive.
SEG1="$(docker exec "$SRC" psql -U postgres -d fuelflow -Atc \
  'SELECT pg_walfile_name(pg_current_wal_lsn())')"
[[ -n "$SEG1" ]] || fail "could not name the current WAL segment."
docker exec "$SRC" psql -U postgres -d fuelflow -Atc 'SELECT pg_switch_wal()' >/dev/null
wait_for_segment "$SEG1" || fail "segment $SEG1 never landed in the archive."

echo "[$(date -Is)] Taking the base backup ..."
docker exec "$SRC" pg_basebackup -U postgres -D - -Ft -X fetch --checkpoint=fast > "$TMP/base.tar"
[[ -s "$TMP/base.tar" ]] || fail "pg_basebackup produced no data."
BASE_NAME="fuelflow_base_$(date -u +%Y-%m-%d_%H%M%S).tar.age"
age -r "$RECIPIENT" -o "$TMP/remote/$BASE_NAME" "$TMP/base.tar"

echo "[$(date -Is)] Writing marker 2 (after the base) and forcing a WAL switch ..."
docker exec "$SRC" psql -U postgres -d fuelflow -v ON_ERROR_STOP=1 -q \
  -c "INSERT INTO drill_marker (id, note) VALUES (2, 'after-base');"
SEG2="$(docker exec "$SRC" psql -U postgres -d fuelflow -Atc \
  'SELECT pg_walfile_name(pg_current_wal_lsn())')"
[[ -n "$SEG2" ]] || fail "could not name the second WAL segment."
docker exec "$SRC" psql -U postgres -d fuelflow -Atc 'SELECT pg_switch_wal()' >/dev/null
wait_for_segment "$SEG2" || fail "segment $SEG2 never landed in the archive."

MARKER2="$(docker exec "$SRC" psql -U postgres -d fuelflow -Atc \
  "SELECT to_char(at AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"') FROM drill_marker WHERE id=2")"

# Ship while the source is still alive - that is what the timer does in production. The
# stop below then happens with the off-box copy already in place, so the restore has no
# way to read anything the source kept.
docker exec "$SRC" chmod -R a+r /var/lib/postgresql/wal
echo "[$(date -Is)] Shipping the archive off-box ..."
SHIP_START="$(date +%s)"
BACKUP_REMOTE="$TMP/remote" WAL_ARCHIVE_DIR="$TMP/archive" \
  WAL_SHIP_STATE_DIR="$TMP/state" WAL_REMOTE_RETENTION_DAYS=999 ENV_FILE="$TMP/env" \
  bash "$SCRIPT_DIR/ship-wal-offbox.sh"
SHIPPED_AT="$(date +%s)"

SEG_MTIME="$(stat -c %Y "$TMP/archive/$SEG2")"
SHIP_LAG=$((SHIPPED_AT - SEG_MTIME))
echo "  shipped in $((SHIPPED_AT - SHIP_START))s; segment $SEG2 was ${SHIP_LAG}s old when it left"

docker stop "$SRC" >/dev/null
echo "[$(date -Is)] Source stopped. This is the host loss."
echo "  last committed row: $MARKER2"

echo "[$(date -Is)] Restoring from the off-box copy only ..."
RESTORE_LOG="$TMP/restore.log"
set +e
AGE_KEY_FILE="$TMP/key.txt" bash "$SCRIPT_DIR/restore-pitr.sh" \
  --base-remote "$TMP/remote" \
  --wal-remote "$TMP/remote" \
  --name "$DST" --port "$PORT" \
  --pg-user postgres --pg-db fuelflow --keep > "$RESTORE_LOG" 2>&1
RESTORE_RC=$?
set -e
cat "$RESTORE_LOG"
RESTORE_DATA_DIR="$(sed -n 's/^  data dir  : //p' "$RESTORE_LOG" | tail -1)"
[[ "$RESTORE_RC" -eq 0 ]] || fail "restore-pitr.sh failed (exit $RESTORE_RC)."

ROWS="$(docker exec "$DST" psql -U postgres -d fuelflow -Atc \
  "SELECT id || '|' || note FROM drill_marker ORDER BY id")"
echo "  recovered rows:"
printf '%s\n' "$ROWS" | sed 's/^/    /'
grep -q '^2|after-base$' <<<"$ROWS" || fail \
  "marker 2 is missing: the restore used the base backup but did not replay the off-box WAL."
grep -q '^1|before-base$' <<<"$ROWS" || fail "marker 1 is missing."

RECOVERED_AT="$(docker exec "$DST" psql -U postgres -d fuelflow -Atc \
  "SELECT to_char(at AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"') FROM drill_marker WHERE id=2")"

echo
echo "HOST-LOSS DRILL PASSED"
echo "  base backup         : $BASE_NAME"
echo "  WAL segments shipped: $(find "$TMP/remote/wal" -maxdepth 1 -name '*.age' -type f | wc -l)"
echo "  last committed row  : $MARKER2"
echo "  recovered through   : $RECOVERED_AT (marker 2 is present)"
echo "  measured ship lag   : ${SHIP_LAG}s (segment archived -> off-box; archive_timeout adds 15 min)"
if (( SHIP_LAG > 900 )); then
  fail "the ship lag (${SHIP_LAG}s) is outside the 15-minute gate."
fi
echo "  The restored cluster was built from the off-box base and WAL only."
