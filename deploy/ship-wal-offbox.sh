#!/usr/bin/env bash
# Ship the WAL archive off-box continuously, one encrypted segment at a time.
#
# WHY THIS EXISTS
# The nightly backup uploads the whole archive as one tarball. That is a copy of the
# history, and it is also a day old: if the host is lost at 18:00, the newest recoverable
# moment is the previous night's blob. On a payment system that is a day of orders. The
# archive itself is already granular - PostgreSQL finishes a segment and writes it to
# /root/fuelflow-wal - so the fix is not a faster tarball, it is shipping each finished
# segment as it appears.
#
# Every run:
#   1. lists the remote wal/ prefix once;
#   2. uploads every complete local segment that is not there yet, encrypted on its own
#      with age to BACKUP_AGE_RECIPIENT (the same public key as the nightly dump);
#   3. writes wal/status.txt with the newest segment, its archive time, the upload time
#      and the lag - so "is it shipping?" has an answer that is not a guess;
#   4. prunes remote segments older than WAL_REMOTE_RETENTION_DAYS (default 31).
#
# It is run by fuelflow-wal-ship.timer every minute. A failed upload exits non-zero, and
# the unit's OnFailure= hook sends the Telegram alert. A quiet database with no new
# segment exits 0: "nothing to ship" is not a failure. The status file is still rewritten,
# so a fresh status is also a heartbeat.
#
# The private key is NOT here. BACKUP_AGE_RECIPIENT is the public key; the restore host
# holds the private one. A compromised server still cannot read what it uploaded.
#
# INSTALL
#   sudo /root/FuelFlow/deploy/install-wal-ship-schedule.sh
#
# MANUAL RUN
#   /root/FuelFlow/deploy/ship-wal-offbox.sh
#   rclone cat r2:fuelflow-backups/wal/status.txt

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="${ENV_FILE:-$SCRIPT_DIR/.env}"
WAL_ARCHIVE_DIR="${WAL_ARCHIVE_DIR:-/root/fuelflow-wal}"
WAL_SHIP_STATE_DIR="${WAL_SHIP_STATE_DIR:-/var/lib/fuelflow}"
WAL_SHIP_MAX_LAG_SECONDS="${WAL_SHIP_MAX_LAG_SECONDS:-900}"
WAL_REMOTE_RETENTION_DAYS="${WAL_REMOTE_RETENTION_DAYS:-31}"
RCLONE="${RCLONE_BIN:-rclone}"
AGE="${AGE_BIN:-age}"

# shellcheck source=load-env.sh
source "$SCRIPT_DIR/load-env.sh"
# shellcheck source=wal-offbox.sh
source "$SCRIPT_DIR/wal-offbox.sh"

if [[ -f "$ENV_FILE" ]]; then
  load_env "$ENV_FILE" || true
fi

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ -n "${BACKUP_REMOTE:-}" ]] || fail "BACKUP_REMOTE is not set (in $ENV_FILE or the environment)."
[[ -n "${BACKUP_AGE_RECIPIENT:-}" ]] || fail "BACKUP_AGE_RECIPIENT is not set (in $ENV_FILE or the environment)."
command -v "$RCLONE" >/dev/null 2>&1 || fail "'$RCLONE' is not installed, so nothing can leave the box."
command -v "$AGE" >/dev/null 2>&1 || fail "'$AGE' is not installed, so nothing can be encrypted."
[[ -d "$WAL_ARCHIVE_DIR" ]] || fail "the WAL archive directory does not exist: $WAL_ARCHIVE_DIR"

umask 077
mkdir -p "$WAL_SHIP_STATE_DIR"
chmod 700 "$WAL_SHIP_STATE_DIR"

# One run at a time. The timer fires every minute; an upload that takes longer than that
# must not overlap the next one and ship the same segment twice.
if command -v flock >/dev/null 2>&1; then
  exec 9>"$WAL_SHIP_STATE_DIR/lock"
  if ! flock -n 9; then
    echo "[$(date -Is)] Another run still holds the lock; skipping this tick."
    exit 0
  fi
fi

TMP="$(mktemp -d "${TMPDIR:-/tmp}/wal-ship.XXXXXX")"
chmod 700 "$TMP"
cleanup() { rm -rf "$TMP"; }
trap cleanup EXIT

REMOTE_DIR="$(wal_remote_wal_dir "$BACKUP_REMOTE")"

# The previous status is how a quiet run keeps the last upload's lag. The lag of the
# newest shipped segment is the number an operator needs; "no new segment" must not
# erase it.
LAST_UPLOAD=""
LAST_ARCHIVED_AT=""
LAST_SHIPPED_AT=""
LAST_LAG=""
if [[ -f "$WAL_SHIP_STATE_DIR/status.txt" ]]; then
  LAST_UPLOAD="$(sed -n 's/^last_upload_segment=//p' "$WAL_SHIP_STATE_DIR/status.txt" | tail -1)"
  LAST_ARCHIVED_AT="$(sed -n 's/^last_upload_archived_at=//p' "$WAL_SHIP_STATE_DIR/status.txt" | tail -1)"
  LAST_SHIPPED_AT="$(sed -n 's/^last_upload_shipped_at=//p' "$WAL_SHIP_STATE_DIR/status.txt" | tail -1)"
  LAST_LAG="$(sed -n 's/^last_upload_lag_seconds=//p' "$WAL_SHIP_STATE_DIR/status.txt" | tail -1)"
fi

# One listing, not one API call per segment. A failure here is not fatal by itself: the
# prefix may not exist on the first run, and the upload below creates it. If the remote
# is really unreachable, the upload fails and the unit alerts.
declare -A REMOTE=()
if LISTING="$("$RCLONE" lsf "$REMOTE_DIR" --files-only 2>/dev/null)"; then
  while IFS= read -r name; do
    [[ -n "$name" ]] || continue
    REMOTE["$name"]=1
  done <<<"$LISTING"
else
  echo "[$(date -Is)] WARNING: could not list $REMOTE_DIR; treating it as empty." >&2
  echo "         If this is not a first run, the upload below will fail and alert." >&2
fi

# The archive directory contains only segments the archiver has FINISHED: PostgreSQL
# writes each one to the archive after it is complete. The segment being written is in
# pg_wal, not here, so no file here is truncated.
SEGMENTS=()
NEWEST_ARCHIVED=""
for f in "$WAL_ARCHIVE_DIR"/0000000*; do
  [[ -f "$f" ]] || continue
  name="$(basename "$f")"
  # PostgreSQL also writes .backup history files here; they are not WAL segments and
  # must not become the "newest archived" number in the status object.
  wal_object_is_segment "$name" || continue
  SEGMENTS+=("$f")
  if [[ "$name" > "$NEWEST_ARCHIVED" ]]; then
    NEWEST_ARCHIVED="$name"
  fi
done

UPLOADED=0
NEWEST_SHIPPED=""

# Anything already off-box counts as shipped, even when it was uploaded by an earlier
# run (or by a hand-run of this script).
for name in "${!REMOTE[@]}"; do
  if [[ "$name" == *.age ]]; then
    segment="${name%.age}"
    if wal_object_is_segment "$segment" && [[ "$segment" > "$NEWEST_SHIPPED" ]]; then
      NEWEST_SHIPPED="$segment"
    fi
  fi
done

for f in "${SEGMENTS[@]}"; do
  name="$(basename "$f")"
  wal_object_is_segment "$name" || continue

  if [[ -n "${REMOTE[$name.age]:-}" ]]; then
    continue
  fi

  mtime="$(stat -c %Y "$f")"
  if ! wal_ship_segment "$f" "$REMOTE_DIR" "$BACKUP_AGE_RECIPIENT" "$TMP"; then
    echo "ERROR: shipping stopped at $name. The next tick retries; this run fails so the" >&2
    echo "       OnFailure alert fires." >&2
    exit 1
  fi
  UPLOADED=$((UPLOADED + 1))
  REMOTE["$name.age"]=1
  if [[ "$name" > "$NEWEST_SHIPPED" ]]; then
    NEWEST_SHIPPED="$name"
  fi
  LAST_UPLOAD="$name"
  LAST_ARCHIVED_AT="$(date -u -d "@$mtime" +%Y-%m-%dT%H:%M:%SZ)"
  SHIPPED_EPOCH="$(date +%s)"
  LAST_SHIPPED_AT="$(date -u -d "@$SHIPPED_EPOCH" +%Y-%m-%dT%H:%M:%SZ)"
  LAST_LAG=$(( SHIPPED_EPOCH - mtime ))
  echo "[$(date -Is)] shipped $name (archived ${LAST_LAG}s ago)"
  if (( LAST_LAG > WAL_SHIP_MAX_LAG_SECONDS )); then
    echo "[$(date -Is)] WARNING: $name was ${LAST_LAG}s old when it left the box" \
         "(threshold ${WAL_SHIP_MAX_LAG_SECONDS}s). It is off-box now; check why the" \
         "shipment was late." >&2
  fi
done

if (( UPLOADED == 0 )); then
  echo "[$(date -Is)] nothing new to ship (${#SEGMENTS[@]} segment(s) in the archive)."
fi

# The status object is written even on a quiet run: its timestamp is the heartbeat that
# says the shipper itself ran, not just the database.
STATUS_TMP="$TMP/status.txt"
{
  printf 'checked_at=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  printf 'host=%s\n' "$(hostname 2>/dev/null || echo unknown-host)"
  printf 'newest_archived_segment=%s\n' "${NEWEST_ARCHIVED:--}"
  printf 'newest_shipped_segment=%s\n' "${NEWEST_SHIPPED:--}"
  printf 'uploaded_now=%s\n' "$UPLOADED"
  printf 'last_upload_segment=%s\n' "${LAST_UPLOAD:--}"
  printf 'last_upload_archived_at=%s\n' "${LAST_ARCHIVED_AT:--}"
  printf 'last_upload_shipped_at=%s\n' "${LAST_SHIPPED_AT:--}"
  printf 'last_upload_lag_seconds=%s\n' "${LAST_LAG:--}"
} > "$STATUS_TMP"

if ! "$RCLONE" copyto "$STATUS_TMP" "$REMOTE_DIR/status.txt"; then
  echo "ERROR: the segments uploaded, but the status object could not be written." >&2
  echo "       That is an observability failure, so this run still fails and alerts." >&2
  exit 1
fi
cp "$STATUS_TMP" "$WAL_SHIP_STATE_DIR/status.txt"

# The off-box copy is the disaster-recovery copy, so it is kept longer than the on-box
# archive (3 days). It is not fatal: the upload above already succeeded, and a transient
# delete failure must not page anyone or discard a good night's copy.
if ! "$RCLONE" delete --min-age "${WAL_REMOTE_RETENTION_DAYS}d" \
     --include '*.age' "$REMOTE_DIR" 2>/dev/null; then
  echo "[$(date -Is)] WARNING: remote prune failed; old segments may accumulate until the" \
       "next successful run." >&2
fi

echo "[$(date -Is)] done: $UPLOADED new segment(s), newest off-box ${NEWEST_SHIPPED:--}."
