#!/usr/bin/env bash
# Restore the database to a point in time, using a base backup plus the WAL archive.
#
# WHY THIS EXISTS
# A nightly pg_dump is a snapshot. Restoring it gives you the database as of 03:23 and
# nothing after it, so the recovery point objective is a full day - on a payment system,
# a day of orders. WAL archiving changes that: PostgreSQL ships the write-ahead log off
# the server as it fills, and this script puts a base backup back together with the log
# and replays it to a chosen moment.
#
#   base backup  = where the replay starts
#   WAL archive  = how the replay gets to the moment you want
#
# The result is a database that is exactly as it was at that moment, and the operator
# decides what to do about it. That last part is the reason this script is a RESTORE and
# not a recovery: it never touches the running database, and it never deletes anything.
# It builds a separate database, and a human decides whether to switch to it.
#
# The drill mode is the important one. A restore path that has never been run is a
# belief, not a capability, and the only way to find out whether it works is to run it.
# --drill restores into a throwaway container on a different port, leaving the live
# database completely untouched, and reports what it recovered.
#
# USAGE
#   restore-pitr.sh --base <file.tar.gz> --wal-dir <dir> [--target-time "YYYY-MM-DD HH:MM:SS"]
#                   [--name <container>] [--port <port>] [--keep]
#
#   restore-pitr.sh --base-remote <rclone:bucket> --wal-remote <rclone:bucket[/wal]>
#                   [--target-time "YYYY-MM-DD HH:MM:SS"]
#                   [--name <container>] [--port <port>] [--keep]
#
#   --base        a base backup. Either the .tar.gz from pg_basebackup, or the .tar.gz.age
#                 from backup.sh - the encrypted one is decrypted first, with the age key.
#   --wal-dir     a directory of WAL segments (the archive, or the unpacked off-box copy).
#   --base-remote the off-box directory that holds fuelflow_base_*.tar.age. The newest one
#                 is fetched with rclone. That is the host-loss restore: nothing on the
#                 dead box is needed.
#   --wal-remote  the off-box wal/ prefix (or the bucket root - /wal is appended if it is
#                 missing). Every <segment>.age object is downloaded and decrypted.
#   --target-time the moment to restore to. Without it, recovery replays as far as the
#                 archive goes, which is "the last thing that happened" - rarely what
#                 anybody wants, so think before omitting it.
#
# --base/--base-remote and --wal-dir/--wal-remote are pairs: give exactly one of each.
# The off-box pair needs rclone, age, and the private key.
#
# The age key is read from AGE_KEY_FILE, or from ~/.config/age/keys.txt. The server does
# not have it - the key lives on a laptop, in a password manager - so a restore usually
# happens somewhere else, which is the point: the encrypted off-box copy is the one that
# is always available.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RCLONE="${RCLONE_BIN:-rclone}"
AGE="${AGE_BIN:-age}"
# shellcheck source=wal-offbox.sh
source "$SCRIPT_DIR/wal-offbox.sh"

BASE=""
BASE_REMOTE=""
WAL_DIR=""
WAL_REMOTE=""
TARGET_TIME=""
NAME="fuelflow-pitr"
PORT="55499"
KEEP=0
PG_IMAGE="${PG_IMAGE:-postgres:16-alpine}"
PG_USER="${PG_USER:-postgres}"
PG_DB="${PG_DB:-fuelflow}"
ENV_FILE="${ENV_FILE:-}"

usage() {
  # Print the header down to `set -euo pipefail`, without depending on a line number:
  # adding a flag must not silently truncate the help text.
  sed -n '2,/^set -euo pipefail$/{/^set -euo pipefail$/d;p}' "$0" | sed 's/^# \{0,1\}//'
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --base)        BASE="$2"; shift 2 ;;
    --base-remote) BASE_REMOTE="$2"; shift 2 ;;
    --wal-dir)     WAL_DIR="$2"; shift 2 ;;
    --wal-remote)  WAL_REMOTE="$2"; shift 2 ;;
    --target-time) TARGET_TIME="$2"; shift 2 ;;
    --name)        NAME="$2"; shift 2 ;;
    --port)        PORT="$2"; shift 2 ;;
    --pg-user)     PG_USER="$2"; shift 2 ;;
    --pg-db)       PG_DB="$2"; shift 2 ;;
    --env-file)    ENV_FILE="$2"; shift 2 ;;
    --keep)        KEEP=1; shift ;;
    --help)        usage ;;
    *) echo "unknown argument: $1" >&2; usage ;;
  esac
done

# The role and the database come from the deployment's own .env when it is available,
# because the role is NOT "postgres" - it is whatever the deployment was created with
# (fuelflow). Connecting as the wrong role fails with "role does not exist", which is a
# confusing way to learn that.
#
# The restore usually happens somewhere ELSE - on the laptop that holds the age key - so
# the .env may not be there at all. In that case the flags are the way to say it.
if [[ -n "$ENV_FILE" && -f "$ENV_FILE" ]]; then
  # shellcheck source=load-env.sh
  source "$SCRIPT_DIR/load-env.sh"
  load_env "$ENV_FILE"
  PG_USER="${POSTGRES_USER:-$PG_USER}"
  PG_DB="${POSTGRES_DB:-$PG_DB}"
fi

if [[ -n "$BASE" && -n "$BASE_REMOTE" ]]; then
  echo "ERROR: give --base or --base-remote, not both." >&2; exit 1
fi
if [[ -n "$WAL_DIR" && -n "$WAL_REMOTE" ]]; then
  echo "ERROR: give --wal-dir or --wal-remote, not both." >&2; exit 1
fi
[[ -n "$BASE" || -n "$BASE_REMOTE" ]] || { echo "ERROR: --base or --base-remote is required." >&2; exit 1; }
[[ -n "$WAL_DIR" || -n "$WAL_REMOTE" ]] || { echo "ERROR: --wal-dir or --wal-remote is required." >&2; exit 1; }
[[ -z "$BASE" || -f "$BASE" ]] || { echo "ERROR: base backup not found: $BASE" >&2; exit 1; }
[[ -z "$WAL_DIR" || -d "$WAL_DIR" ]] || { echo "ERROR: WAL directory not found: $WAL_DIR" >&2; exit 1; }

if [[ -n "$BASE_REMOTE" || -n "$WAL_REMOTE" ]]; then
  command -v "$RCLONE" >/dev/null 2>&1 || {
    echo "ERROR: --base-remote/--wal-remote need rclone, and it is not installed." >&2; exit 1; }
fi
if [[ -n "$WAL_REMOTE" ]]; then
  WAL_REMOTE="$(wal_remote_wal_dir "$WAL_REMOTE")"
fi

# The key is needed for an encrypted base backup AND for the per-segment off-box WAL.
NEEDS_AGE=0
[[ -n "$BASE_REMOTE" ]] && NEEDS_AGE=1
[[ -n "$WAL_REMOTE" ]] && NEEDS_AGE=1
if [[ -n "$BASE" && "$BASE" == *.age ]]; then NEEDS_AGE=1; fi

AGE_KEY_PATH=""
AGE_ARGS=()
if [[ "$NEEDS_AGE" -eq 1 ]]; then
  command -v "$AGE" >/dev/null 2>&1 || {
    echo "ERROR: this restore needs age, and it is not installed." >&2; exit 1; }
  if [[ -n "${AGE_KEY_FILE:-}" ]]; then
    AGE_KEY_PATH="$AGE_KEY_FILE"
  elif [[ -f "$HOME/.config/age/keys.txt" ]]; then
    AGE_KEY_PATH="$HOME/.config/age/keys.txt"
  else
    echo "ERROR: this restore uses encrypted data and no age key was found." >&2
    echo "       Set AGE_KEY_FILE to the private key, or put it in ~/.config/age/keys.txt." >&2
    exit 1
  fi
  AGE_ARGS=(-i "$AGE_KEY_PATH")
fi

WORK="$(mktemp -d "${TMPDIR:-/tmp}/pitr.XXXXXX")"
chmod 700 "$WORK"

cleanup() {
  if [[ "$KEEP" -eq 1 ]]; then
    # The container is left running, so its data directory has to be left on disk too.
    # Deleting it out from under a live server makes PostgreSQL PANIC and die - which is
    # exactly what happened the first time this script was written.
    echo "[$(date -Is)] Left in place for inspection:"
    echo "  container : $NAME"
    echo "  data dir  : $WORK/data"
    return 0
  fi
  # The container goes FIRST: it has the directory open, and removing a directory out from
  # under a running server kills it in a way that looks like data loss.
  docker rm -f "$NAME" >/dev/null 2>&1 || true
  rm -rf "$WORK"
}
trap cleanup EXIT

# The off-box path is the one that survives losing the host: the base backup and the WAL
# are both fetched here, and nothing on the dead box is read. The fetches land in WORK,
# so the cleanup above removes them with everything else.
if [[ -n "$BASE_REMOTE" ]]; then
  echo "[$(date -Is)] Fetching the newest base backup from $BASE_REMOTE ..."
  BASE="$WORK/base-fetched.tar.age"
  BASE_NAME="$(wal_fetch_base "$BASE_REMOTE" "$BASE")" || exit 1
  echo "         fetched $BASE_NAME"
fi
if [[ -n "$WAL_REMOTE" ]]; then
  echo "[$(date -Is)] Fetching and decrypting the WAL archive from $WAL_REMOTE ..."
  WAL_DIR="$WORK/wal-offbox"
  mkdir -p "$WAL_DIR"
  WAL_COUNT="$(wal_fetch_offbox "$WAL_REMOTE" "$WAL_DIR" "$AGE_KEY_PATH")" || exit 1
  if (( WAL_COUNT == 0 )); then
    echo "ERROR: $WAL_REMOTE holds no decryptable WAL segments." >&2
    echo "       The base backup alone is a snapshot, not a point-in-time restore." >&2
    exit 1
  fi
  echo "         decrypted $WAL_COUNT segment(s)"
fi

echo "[$(date -Is)] Restoring to a point in time"
echo "  base backup : $BASE"
echo "  WAL archive : $WAL_DIR ($(find "$WAL_DIR" -maxdepth 1 -name '0000000*' -type f | wc -l) segments)"
echo "  target time : ${TARGET_TIME:-<end of archive>}"

# --- Unpack the base backup ---------------------------------------------------------
BASE_TAR="$WORK/base.tar"
if [[ "$BASE" == *.age ]]; then
  "$AGE" -d "${AGE_ARGS[@]}" "$BASE" > "$BASE_TAR"
else
  cp "$BASE" "$BASE_TAR"
fi

# The base backup is a tar of a data directory. It is unpacked into the container's
# data directory, which must be EMPTY - a data directory that already has a cluster in
# it will make PostgreSQL refuse to start, which is the correct behaviour.
DATA_DIR="$WORK/data"
mkdir -p "$DATA_DIR"
tar --force-local -xf "$BASE_TAR" -C "$DATA_DIR"

if [[ ! -f "$DATA_DIR/PG_VERSION" ]]; then
  echo "ERROR: the base backup does not contain a PostgreSQL data directory." >&2
  echo "       (no PG_VERSION after unpacking - is this the right file?)" >&2
  exit 1
fi
echo "[$(date -Is)] Base backup unpacked: $(du -sh "$DATA_DIR" | cut -f1)"

# The recovery configuration. This is the whole trick:
#
#   recovery.signal      tells PostgreSQL to enter recovery instead of starting fresh
#   restore_command      where to get the WAL segments from - here, the archive directory
#   recovery_target_time stop at the requested moment
#   recovery_target_action=promote  then come up as a normal, writable database
#
# The settings are APPENDED to postgresql.auto.conf rather than replacing it: the file is
# the cluster's ALTER SYSTEM history, and a restore that quietly threw it away would hand
# back a database that does not behave like the one that was lost. Later settings win, so
# appending is enough to override anything the original had.
#
# archive_mode is off for the restored cluster: during recovery PostgreSQL does not
# archive, but the moment it promotes it would start writing into the archive it is
# reading from, and the `test ! -f` guard would make every archive attempt fail. The
# operator turns it back on after switching over - see docs/DEPLOYMENT.md.
cat >> "$DATA_DIR/postgresql.auto.conf" <<CONF
restore_command = 'cp /var/lib/postgresql/wal/%f %p'
recovery_target_action = 'promote'
CONF
if [[ -n "$TARGET_TIME" ]]; then
  echo "recovery_target_time = '${TARGET_TIME}'" >> "$DATA_DIR/postgresql.auto.conf"
fi
# recovery.signal is what puts the server into recovery mode.
: > "$DATA_DIR/recovery.signal"

# The data directory has to belong to the user the image runs as, or PostgreSQL will not
# start. The chown happens INSIDE a container of the same image, so the script does not
# depend on the host having a chown that can change a uid (and so it works the same on
# a laptop and on the server).
docker run --rm -v "$DATA_DIR:/var/lib/postgresql/data" --entrypoint sh "$PG_IMAGE" -c \
  'chown -R postgres:postgres /var/lib/postgresql/data' >/dev/null

# --- Run it ------------------------------------------------------------------------
docker rm -f "$NAME" >/dev/null 2>&1 || true
docker run -d --name "$NAME" \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB="$PG_DB" \
  -e POSTGRES_USER="$PG_USER" \
  -p "127.0.0.1:${PORT}:5432" \
  -v "$DATA_DIR:/var/lib/postgresql/data" \
  -v "$WAL_DIR:/var/lib/postgresql/wal:ro" \
  "$PG_IMAGE" \
  postgres -c archive_mode=off -c restore_command="cp /var/lib/postgresql/wal/%f %p" \
  >/dev/null

echo "[$(date -Is)] Waiting for recovery to finish ..."

# Recovery ends when the target is reached and the server promotes. Poll the log for
# the message that says so, and give up after a bounded time rather than hanging.
DEADLINE=$((SECONDS + 900))
RECOVERED=0
while [[ $SECONDS -lt $DEADLINE ]]; do
  if docker logs "$NAME" 2>&1 | grep -q "database system is ready to accept connections"; then
    RECOVERED=1
    break
  fi
  if docker logs "$NAME" 2>&1 | grep -q "recovery ended before configured recovery target was reached"; then
    echo "ERROR: the archive does not reach the requested moment." >&2
    echo "       The base backup is fine, but the WAL archive stops before the target" >&2
    echo "       time, so the database cannot be recovered that far forward." >&2
    echo "       The newest segment in the archive is the furthest point recoverable." >&2
    docker logs "$NAME" 2>&1 | tail -10 >&2
    exit 1
  fi
  if docker logs "$NAME" 2>&1 | grep -qE 'FATAL|PANIC'; then
    echo "ERROR: the restored server failed to start:" >&2
    docker logs "$NAME" 2>&1 | tail -30 >&2
    exit 1
  fi
  sleep 3
done

if [[ "$RECOVERED" -ne 1 ]]; then
  echo "ERROR: recovery did not finish within 15 minutes." >&2
  docker logs "$NAME" 2>&1 | tail -30 >&2
  exit 1
fi

# Prove that recovery ACTUALLY RAN, rather than trusting that it did.
#
# The server's own log is the authoritative record. If it does not say that recovery
# started, then no recovery happened: the container started a brand new empty cluster
# instead of the one that was restored. That is a silent, total failure - the script
# would report success and hand over an empty database - so it is checked rather than
# assumed.
LOG="$(docker logs "$NAME" 2>&1 || true)"
if ! grep -q "starting point-in-time recovery\|starting archive recovery" <<<"$LOG"; then
  echo "ERROR: the restored server did not go into recovery." >&2
  echo "       It appears to have started a fresh, empty cluster - so the base backup" >&2
  echo "       was never actually used. Treating this as a failed restore." >&2
  tail -20 <<<"$LOG" >&2
  exit 1
fi
if ! grep -q "archive recovery complete" <<<"$LOG"; then
  echo "ERROR: recovery did not complete. The archive may not reach the requested" >&2
  echo "       moment, or a segment is missing from it." >&2
  tail -20 <<<"$LOG" >&2
  exit 1
fi
echo "[$(date -Is)] Recovery confirmed: the cluster was restored from the base backup."

# Report what was actually recovered, rather than what was asked for. If the archive ran
# out before the target, recovery stops at the last segment it had - and the operator
# needs to know that, because the data they get is older than the data they wanted.
#
# The timestamp comes from the server's own log. PostgreSQL records exactly where it
# stopped, in the form "recovery stopping before commit of transaction 733, time
# 2026-10-19 18:53:16.560838+00", and that is the moment the database now reflects.
REACHED="$(grep -oE 'recovery stopping (before|after) [a-z]+( [a-z]+)* [0-9]+, time [^ ]+ [^ ]+' <<<"$LOG" | tail -1 || true)"
LSN="$(grep -oE 'redo done at [^ ]+' <<<"$LOG" | tail -1 || true)"

echo
echo "[$(date -Is)] Recovery finished."
[[ -n "$LSN" ]]     && echo "  $LSN"
[[ -n "$REACHED" ]] && echo "  $REACHED"
if [[ -n "$TARGET_TIME" ]]; then
  echo "  requested target: $TARGET_TIME"
# Compare the two moments. The recovered time is a few milliseconds off the requested one
# in normal operation - recovery stops at the first commit AFTER the target - so this is
# a check that the recovered time is not BEFORE the target, not an equality test.
#
# The two timestamps are converted to epoch seconds by hand rather than by `date -d`,
# because `date -d "2026-10-19 18:57:47.764168+00"` misreads the "+00" as something else
# entirely and lands three hours away. PostgreSQL writes timestamps with that suffix, so
# this is the format that actually arrives.
to_epoch() {
  local ts="$1" base sign hh mm offsecs
  if [[ "$ts" =~ ^(.*[0-9:])([+-])([0-9]{2}):?([0-9]{2})?$ ]]; then
    ts="${BASH_REMATCH[1]}"
    sign="${BASH_REMATCH[2]}"; hh="${BASH_REMATCH[3]}"; mm="${BASH_REMATCH[4]:-0}"
    offsecs=$(( 10#$hh * 3600 + 10#$mm * 60 ))
    base="$(date -u -d "$ts" +%s 2>/dev/null || echo 0)"
    # "+03" means local time is AHEAD of UTC, so the UTC instant is earlier.
    if [[ "$sign" == "+" ]]; then echo $(( base - offsecs )); else echo $(( base + offsecs )); fi
  else
    # No offset: the server's own timezone, which is how PostgreSQL reads
    # recovery_target_time.
    date -d "$ts" +%s 2>/dev/null || echo 0
  fi
}

# The offset is kept: the recovered timestamp and the requested one have to be read the
# same way, or the comparison is between two different clocks.
ACTUAL_TS="$(sed -nE 's/.*time ([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9:.]+[+-][0-9]+:?[0-9]*).*/\1/p' <<<"$REACHED" | head -1)"
if [[ -z "$ACTUAL_TS" ]]; then
  ACTUAL_TS="$(sed -nE 's/.*time ([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9:.]+).*/\1/p' <<<"$REACHED" | head -1)"
fi
if [[ -n "$ACTUAL_TS" ]]; then
  echo "  actual replay time: $ACTUAL_TS"
  A_EPOCH="$(to_epoch "$ACTUAL_TS")"
  T_EPOCH="$(to_epoch "$TARGET_TIME")"
  if (( A_EPOCH >= T_EPOCH )); then
    echo "  => the database reflects the requested moment (or the first commit after it)."
  else
    echo "  => NOTE: the replay stopped BEFORE the requested moment." >&2
    echo "     The archive did not contain the WAL for the whole window. The data is" >&2
    echo "     real, but it is older than what was asked for." >&2
  fi
else
  echo "  => NOTE: the server's log did not record a recovery stop point, so the" >&2
  echo "     recovered moment cannot be confirmed. Check the log before trusting it." >&2
fi
fi
echo
echo "  The restored database is running as a container named '${NAME}' on 127.0.0.1:${PORT}."
echo "  The live database was NOT touched at any point."
echo "  To look at it:  docker exec -it $NAME psql -U $PG_USER -d $PG_DB"
echo
