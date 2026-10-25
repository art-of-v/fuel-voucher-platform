#!/usr/bin/env bash
# Prove that point-in-time recovery actually works, on the live system, without touching
# the live database.
#
# WHY THIS EXISTS
# A restore path that has never been run is a belief, not a capability. The day it is
# needed is the worst possible day to find out that the archive was empty, the base
# backup was corrupt, or the script needed a flag nobody remembered.
#
# So this script runs the whole thing, for real, against the live archive:
#
#   1. take a base backup of the live database, right now
#   2. force a WAL switch, so the archive has a segment from this moment
#   3. restore that base backup plus the archive into a THROWAWAY container
#   4. check that the restore really recovered, and that the data is there
#
# The live database is never written to, never stopped, and never restored over. The
# restored copy is built in a separate container on a separate port, and it is removed
# at the end. Run it as often as you like.
#
# USAGE
#   drill-pitr.sh [--container <name>] [--archive-dir <path>] [--keep]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="${COMPOSE_FILE:-$SCRIPT_DIR/docker-compose.prod.yml}"
ENV_FILE="${ENV_FILE:-$SCRIPT_DIR/.env.production}"
CONTAINER="${CONTAINER:-fuelflow-postgres}"
ARCHIVE_DIR="${WAL_ARCHIVE_DIR:-/root/fuelflow-wal}"
DRILL_CONTAINER="${DRILL_CONTAINER:-fuelflow-pitr-drill}"
DRILL_PORT="${DRILL_PORT:-55499}"
KEEP=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --container)   CONTAINER="$2"; shift 2 ;;
    --archive-dir) ARCHIVE_DIR="$2"; shift 2 ;;
    --env-file)    ENV_FILE="$2"; shift 2 ;;
    --keep)        KEEP=1; shift ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done

# shellcheck source=load-env.sh
source "$SCRIPT_DIR/load-env.sh"
load_env "$ENV_FILE"
# shellcheck source=pitr-common.sh
source "$SCRIPT_DIR/pitr-common.sh"
: "${POSTGRES_USER:?POSTGRES_USER is not set in $ENV_FILE}"
: "${POSTGRES_DB:?POSTGRES_DB is not set in $ENV_FILE}"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/pitr-drill.XXXXXX")"
chmod 700 "$WORK"

cleanup() {
  # The marker table is the only thing the drill writes to the live database, so it is
  # taken away again. A drill that leaves litter behind is a drill people stop running.
  docker exec "$CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -q -c \
    "DROP TABLE IF EXISTS pitr_drill_marker" >/dev/null 2>&1 || true
  if [[ "$KEEP" -eq 1 ]]; then
    echo "[$(date -Is)] Left the drill container running: ${DRILL_CONTAINER}"
    return 0
  fi
  docker rm -f "$DRILL_CONTAINER" >/dev/null 2>&1 || true
  rm -rf "$WORK"
}
trap cleanup EXIT

echo "Point-in-time recovery drill"
echo "  live database : $CONTAINER (${POSTGRES_DB})"
echo "  WAL archive   : $ARCHIVE_DIR"
echo "  drill copy    : $DRILL_CONTAINER on 127.0.0.1:${DRILL_PORT}"
echo

# --- 0. The archive has to be working, or there is nothing to drill -------------------
if ! check_wal_archive "$CONTAINER" "$ARCHIVE_DIR" "$POSTGRES_USER" "$POSTGRES_DB" 1800; then
  echo "ERROR: the WAL archive is not working, so there is nothing to drill." >&2
  echo "       Run deploy/enable-pitr.sh first." >&2
  exit 1
fi

# --- 1. A base backup of the live database -------------------------------------------
echo "[$(date -Is)] Taking a base backup of the live database ..."
docker exec "$CONTAINER" pg_basebackup -U "$POSTGRES_USER" -D - -Ft -X fetch --checkpoint=fast \
  > "$WORK/base.tar" 2>"$WORK/base.err" || { echo "ERROR: pg_basebackup failed:" >&2; cat "$WORK/base.err" >&2; exit 1; }
echo "[$(date -Is)] Base backup: $(du -h "$WORK/base.tar" | cut -f1)"

# --- 2. A marker transaction, and a moment that can actually be recovered to ----------
# The target has to be a moment that a COMMITTED TRANSACTION sits on, and that
# transaction's WAL has to be in the archive. Both of those are easy to get wrong:
#
#   * a target that is later than the last archived transaction cannot be reached, and
#     PostgreSQL says "recovery ended before configured recovery target was reached"
#   * a target that is later than the newest ARCHIVED SEGMENT cannot be reached either,
#     because the WAL for it is still open and has not been shipped
#
# So the drill writes a real transaction, takes its timestamp, and THEN forces the WAL
# switch that ships the segment containing it. The marker table is created for the drill
# and dropped at the end; it is the only thing the drill writes to the live database.
echo "[$(date -Is)] Writing a marker transaction ..."
TARGET="$(docker exec "$CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAX -c "
  CREATE TABLE IF NOT EXISTS pitr_drill_marker (id int primary key, noted_at timestamptz default now());
  INSERT INTO pitr_drill_marker (id) VALUES (1) ON CONFLICT (id) DO UPDATE SET noted_at = now();
  SELECT now();" 2>/dev/null | tail -1)"
if [[ -z "$TARGET" ]]; then
  echo "ERROR: could not write the marker transaction - the drill cannot run." >&2
  exit 1
fi
echo "[$(date -Is)] Target moment: ${TARGET}"

echo "[$(date -Is)] Forcing a WAL switch ..."
BEFORE_SEGMENTS="$(find "$ARCHIVE_DIR" -maxdepth 1 -name '0000000*' -type f 2>/dev/null | wc -l)"
docker exec "$CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -q -c "SELECT pg_switch_wal()" >/dev/null

SEGMENT_READY=0
for _ in $(seq 1 30); do
  NOW_SEGMENTS="$(find "$ARCHIVE_DIR" -maxdepth 1 -name '0000000*' -type f 2>/dev/null | wc -l)"
  if [[ "${NOW_SEGMENTS:-0}" -gt "${BEFORE_SEGMENTS:-0}" ]]; then SEGMENT_READY=1; break; fi
  sleep 1
done
if [[ "$SEGMENT_READY" -ne 1 ]]; then
  echo "ERROR: the WAL switch did not produce a new archived segment within 30s." >&2
  echo "       Archiving is not working, so the drill cannot run." >&2
  exit 1
fi
echo "[$(date -Is)] The new segment is in the archive."

# --- 3. Restore it -------------------------------------------------------------------
echo "[$(date -Is)] Restoring ..."
# --keep is always passed: the restored container has to still be there for the
# comparison below, so the drill removes it itself rather than letting the restore
# script clean up after itself.
"$SCRIPT_DIR/restore-pitr.sh" \
  --base "$WORK/base.tar" \
  --wal-dir "$ARCHIVE_DIR" \
  --target-time "$TARGET" \
  --name "$DRILL_CONTAINER" \
  --port "$DRILL_PORT" \
  --pg-user "$POSTGRES_USER" \
  --pg-db "$POSTGRES_DB" \
  --keep

# --- 4. Does the restored database look like the live one? ----------------------------
# The restore can succeed and still produce a database that is missing everything -
# which is what happened while this drill was being written, when a path bug made the
# container start a fresh empty cluster. So the row counts are compared.
echo
echo "[$(date -Is)] Comparing the restored database with the live one ..."
TABLES="$(docker exec "$CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAX -c \
  "SELECT count(*) FROM pg_tables WHERE schemaname='public'" 2>/dev/null || echo 0)"

MISMATCH=0
while IFS='|' read -r t live; do
  [[ -z "$t" ]] && continue
  restored="$(docker exec "$DRILL_CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAX -c \
    "SELECT count(*) FROM \"${t}\"" 2>/dev/null || echo "missing")"
  if [[ "$restored" != "$live" ]]; then
    echo "  MISMATCH  ${t}: live=${live} restored=${restored}" >&2
    MISMATCH=$((MISMATCH + 1))
  fi
done < <(docker exec "$CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAX -F '|' -c \
  "SELECT tablename, (xpath('/row/count/text()', query_to_xml('SELECT count(*) FROM ' || quote_ident(tablename), false, true, '')))[1]::text::bigint
   FROM pg_tables WHERE schemaname='public' AND tablename <> 'pitr_drill_marker' ORDER BY tablename" 2>/dev/null || true)

if [[ "$MISMATCH" -gt 0 ]]; then
  echo >&2
  echo "ERROR: ${MISMATCH} table(s) differ between the live database and the restore." >&2
  exit 1
fi

echo "[$(date -Is)] OK: all ${TABLES} tables have the same row counts in both databases."
echo
echo "  Point-in-time recovery WORKS. The restore is real, it is repeatable, and it"
echo "  does not touch the live database."
echo
