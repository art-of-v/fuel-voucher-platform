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
#   2. uploads every COMPLETE local segment that is not there yet, encrypted on its own
#      with age to BACKUP_AGE_RECIPIENT (the same public key as the nightly dump);
#   3. re-uploads any segment whose off-box object is shorter than the segment itself -
#      a half-written upload that was never replaced;
#   4. writes wal/status.txt with the newest segment, its archive time, the upload time
#      and the lag - so "is it shipping?" has an answer that is not a guess.
#
# It does NOT prune, and that is deliberate. A prune is a full scan of the wal/ prefix and
# costs about a tenth of a second per object: measured on the production box on
# 2026-10-29, 60-105 s for 707 objects to delete NOTHING (the oldest segment was six days
# old), and several minutes when a connection stalled. Under this unit's 600 s timeout
# that failed runs whose shipment had already succeeded - 30+ OnFailure alerts in one day -
# and at the 31 days of retention the bucket is sized for it extrapolates to five minutes
# or more, over the budget on its own. Retention is the nightly backup's job
# (deploy/backup.sh); this script must stay proportional to the number of NEW segments.
#
# "Complete" is checked, not assumed: the archiver's cp writes a segment into the
# archive directory in place, so the file is briefly shorter than a segment while it is
# being copied. Uploading that would put a hole in the off-box chain, and a restore
# stops at a hole without saying why.
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
ENV_FILE="${ENV_FILE:-$SCRIPT_DIR/.env.production}"
WAL_ARCHIVE_DIR="${WAL_ARCHIVE_DIR:-/root/fuelflow-wal}"
WAL_SHIP_STATE_DIR="${WAL_SHIP_STATE_DIR:-/var/lib/fuelflow}"
WAL_SHIP_MAX_LAG_SECONDS="${WAL_SHIP_MAX_LAG_SECONDS:-900}"
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
if LISTING="$("$RCLONE" lsf "$REMOTE_DIR" --files-only "${WAL_RCLONE_TIMEOUT[@]}" 2>/dev/null)"; then
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
#
# THAT LAST SENTENCE WAS WRONG, AND THE REAL-R2 DRILL PROVED IT. The archive_command is
# "test ! -f %f && cp %p %f", and that cp writes the segment into the archive directory
# IN PLACE: the file name appears from the first byte and the file grows to 16 MiB over
# the next moments. A shipper that picks it up in that window uploads a TRUNCATED
# segment - which decrypts, looks right, and is never uploaded again, and which stops a
# restore replaying at exactly that point, silently.
#
# So the segments are checked, not trusted: only a file of exactly WAL_SEGMENT_SIZE bytes
# is a finished segment, and the ship stops at the first one that is not (it does not
# skip ahead, because a hole in the sequence stops a restore just as hard).
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
REPAIRED=0
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

# The sizes of what is off-box, so a short object can be spotted. A half-written segment
# that was uploaded once is never replaced by the ordinary "not there yet" path: it IS
# there, by name, so it would sit in the bucket forever with a hole in the chain.
declare -A REMOTE_SIZES=()
while read -r rname rsize; do
  [[ -n "$rname" && -n "$rsize" ]] || continue
  REMOTE_SIZES["$rname"]="$rsize"
done < <(wal_remote_object_sizes "$REMOTE_DIR" || true)

for f in "${SEGMENTS[@]}"; do
  name="$(basename "$f")"
  wal_object_is_segment "$name" || continue

  # A segment that is still being copied is not a segment yet. Stop here: the next tick
  # will pick it up, whole.
  if ! wal_segment_is_complete "$f"; then
    echo "[$(date -Is)] $name is still being written ($(wal_segment_size "$f") of $WAL_SEGMENT_SIZE bytes);" \
         "the next tick ships it." >&2
    break
  fi

  local_size="$(wal_segment_size "$f")"

  if [[ -n "${REMOTE[$name.age]:-}" ]]; then
    # It is off-box. Is it WHOLE? The ciphertext is larger than the plaintext, so a
    # shorter object is a half-written upload: re-upload it, or the chain stays broken.
    # An UNKNOWN size is not evidence of anything, so it is left alone.
    remote_size="${REMOTE_SIZES[$name.age]:-}"
    if [[ -n "$remote_size" ]] && (( remote_size < local_size )); then
      echo "[$(date -Is)] $name.age off-box is $remote_size bytes, the segment is $local_size;" \
           "re-uploading it." >&2
      if ! wal_ship_segment "$f" "$REMOTE_DIR" "$BACKUP_AGE_RECIPIENT" "$TMP"; then
        echo "ERROR: could not re-upload $name.age. The next tick retries." >&2
        exit 1
      fi
      REPAIRED=$((REPAIRED + 1))
    fi
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

if (( UPLOADED == 0 && REPAIRED == 0 )); then
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
  printf 'repaired_now=%s\n' "$REPAIRED"
  printf 'last_upload_segment=%s\n' "${LAST_UPLOAD:--}"
  printf 'last_upload_archived_at=%s\n' "${LAST_ARCHIVED_AT:--}"
  printf 'last_upload_shipped_at=%s\n' "${LAST_SHIPPED_AT:--}"
  printf 'last_upload_lag_seconds=%s\n' "${LAST_LAG:--}"
} > "$STATUS_TMP"

if ! "$RCLONE" copyto "$STATUS_TMP" "$REMOTE_DIR/status.txt" "${WAL_RCLONE_TIMEOUT[@]}"; then
  echo "ERROR: the segments uploaded, but the status object could not be written." >&2
  echo "       That is an observability failure, so this run still fails and alerts." >&2
  exit 1
fi
cp "$STATUS_TMP" "$WAL_SHIP_STATE_DIR/status.txt"

echo "[$(date -Is)] done: $UPLOADED new segment(s), newest off-box ${NEWEST_SHIPPED:--}."
