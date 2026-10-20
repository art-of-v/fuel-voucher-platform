#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Trims the WAL archive, on its own schedule, independently of the nightly backup.
#
#     /root/FuelFlow/deploy/prune-wal-archive.sh
#
# WHY THIS EXISTS
#
# The archive has to be trimmed, or it fills the disk, and a full disk is how the
# database stops accepting writes:
#
#   FATAL: could not write lock file "postmaster.pid": No space left on device
#
# That is not hypothetical on this box: it happened on 2026-09-27 (planning #55).
#
# The trimming itself is not new. deploy/backup.sh has called the same
# prune_wal_archive() with the same bounds since PITR was switched on, and
# deploy/pitr-common.test.sh has been exercising that function all along. What is
# new here is WHEN it runs.
#
# The problem with it living inside backup.sh is the order of the two facts:
#
#   - backup.sh runs `set -euo pipefail` (line 43), and
#   - the prune is at line 350, AFTER the dump (154), the base backup (192-222) and
#     the off-box WAL upload (295-307).
#
# So the thing that protects the disk is the LAST thing a long script does. If any
# step above it fails - an rclone upload, a pg_verifybackup, a full disk that makes
# the dump fail - the script exits and the prune never runs that night. And the
# night the backup breaks is exactly the night the archive stops being trimmed,
# because the archive is what the backup uploads.
#
# The 8 GiB size cap makes a runaway archive impossible, and that cap is enforced
# by this same function. But a cap that is only enforced by a job whose failure
# mode is "the disk is filling up" is not a cap, it is a hope. This script is the
# cap, on its own timer, every six hours, with no database, no network and no age
# key - so it works on the night the backup does not.
#
# WHAT IT DOES
#   - prunes the production archive (age bound + size cap) and the staging one
#     (tighter: a day and a gibibyte). Both, because they SHARE THE DISK - a
#     staging archive that fills the disk takes production down with it.
#   - then checks the filesystem the archive lives on, and EXITS NON-ZERO if it is
#     still at or above the alert threshold. That is what fires the Telegram alert
#     (fuelflow-prune-alert@): disk filling from something the prune cannot
#     reclaim is a page, not a silent outage-in-waiting.
#
# It touches nothing but the two archive directories. It never removes the newest
# segment, because that is the one being written to.
#
# Installed as a timer:  deploy/install-wal-prune-schedule.sh
# One-off run:          cd /root/FuelFlow/deploy && ./prune-wal-archive.sh
# See docs/DEPLOYMENT.md (private docs repo) ("Point-in-time recovery").
# ------------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# The bounds. These are the same numbers backup.sh uses, and they are the numbers
# the policy in backup.sh's comments is written in: the on-box window is short and
# the size cap is the hard guarantee. Changing one without the other is how the
# archive and the disk stop agreeing.
WAL_ARCHIVE_DIR="${WAL_ARCHIVE_DIR:-/root/fuelflow-wal}"
WAL_RETENTION_DAYS="${WAL_RETENTION_DAYS:-3}"
WAL_MAX_BYTES="${WAL_MAX_BYTES:-8589934592}"                 # 8 GiB
STAGING_WAL_ARCHIVE_DIR="${STAGING_WAL_ARCHIVE_DIR:-/root/fuelflow-wal-staging}"
STAGING_WAL_MAX_BYTES="${STAGING_WAL_MAX_BYTES:-1073741824}" # 1 GiB

# Percentage use of the filesystem the archive lives on, at or above which we page
# AFTER pruning. Same threshold and the same meaning as prune-docker.sh's.
DISK_ALERT_THRESHOLD="${DISK_ALERT_THRESHOLD:-85}"

# The filesystem to watch. Derived from the archive directory unless overridden -
# the test points it somewhere harmless, and an operator with the archive on a
# different mount can set it here.
WATCH_PATH="${WATCH_PATH:-}"

log() { echo "[$(date -Is)] $*"; }

# The .env is loaded for the same reason backup.sh loads it: an operator who
# overrides a path or a bound must not get two different answers from two scripts.
# It is OPTIONAL here - this script has safe defaults for everything it needs, and
# a missing .env must not stop the disk from being trimmed. load_env parses the
# file; it never sources it, so a value with a space in it cannot break this.
if [[ -f "$SCRIPT_DIR/.env" ]]; then
  if [[ -f "$SCRIPT_DIR/load-env.sh" ]]; then
    # shellcheck source=load-env.sh
    source "$SCRIPT_DIR/load-env.sh"
    load_env "$SCRIPT_DIR/.env" || log "WARNING: could not read $SCRIPT_DIR/.env - using the built-in defaults."
  else
    log "WARNING: no $SCRIPT_DIR/load-env.sh - using the built-in defaults."
  fi
else
  log "NOTE: no $SCRIPT_DIR/.env - using the built-in defaults."
fi

# shellcheck source=pitr-common.sh
source "$SCRIPT_DIR/pitr-common.sh"

fail() { echo "ERROR: $*" >&2; exit 1; }

# `df -P` forces portable single-line output; column 5 is "Use%" like "83%".
disk_use_pct() {
  local path="$1"
  df -P "$path" 2>/dev/null | awk 'NR==2 { gsub(/%/,"",$5); print $5 }'
}

# The bytes a directory holds, as a plain integer. `du -sb` is what
# prune_wal_archive itself measures against the cap, so this is the same number.
dir_bytes() {
  local dir="$1"
  du -sb "$dir" 2>/dev/null | cut -f1
}

# --- prune one archive ---------------------------------------------------------
#
# The bound is applied in prune_wal_archive: delete anything older than the age
# bound, then, while the directory is over the size cap, delete the oldest segment
# - never the newest one.
prune_one() {
  local dir="$1" age_days="$2" max_bytes="$3" label="$4"

  if [[ ! -d "$dir" ]]; then
    log "${label}: no archive at ${dir} - nothing to do."
    return 0
  fi

  local before after
  before="$(dir_bytes "$dir")"
  before="${before:-0}"

  prune_wal_archive "$dir" "$age_days" "$max_bytes"

  after="$(dir_bytes "$dir")"
  after="${after:-0}"

  local freed=$(( before - after ))
  if (( freed > 0 )); then
    log "${label}: trimmed ${dir} - freed $(( freed / 1024 / 1024 )) MiB, now $(( after / 1024 / 1024 )) MiB."
  else
    log "${label}: ${dir} is already within its bounds ($(( after / 1024 / 1024 )) MiB)."
  fi
}

log "WAL archive prune starting."

prune_one "$WAL_ARCHIVE_DIR" "$WAL_RETENTION_DAYS" "$WAL_MAX_BYTES" "production"
prune_one "$STAGING_WAL_ARCHIVE_DIR" 1 "$STAGING_WAL_MAX_BYTES" "staging"

# --- and then say so if the disk is still tight --------------------------------
#
# The prune can only reclaim the archive. If the disk is still above the
# threshold afterwards, something else is filling it - the same thing
# prune-docker.sh exists for, and the same reason this exits non-zero: the unit
# fails, the alert fires, and a human looks at it. Silently succeeding here would
# turn a disk problem into a discovery.
if [[ -z "$WATCH_PATH" ]]; then
  if [[ -d "$WAL_ARCHIVE_DIR" ]]; then
    WATCH_PATH="$(dirname "$WAL_ARCHIVE_DIR")"
  else
    WATCH_PATH="/"
  fi
fi

used="$(disk_use_pct "$WATCH_PATH")"
[[ -n "$used" ]] || fail "could not read disk usage for ${WATCH_PATH}."

if (( used >= DISK_ALERT_THRESHOLD )); then
  echo "ERROR: ${WATCH_PATH} is ${used}% used after trimming the WAL archive (threshold ${DISK_ALERT_THRESHOLD}%)." >&2
  echo "       The archive is within its bounds, so something ELSE is filling the disk." >&2
  echo "       The nightly Docker GC (deploy/prune-docker.sh) reclaims images and build" >&2
  echo "       cache; if the number is still high after that, it is a volume - and the" >&2
  echo "       database's own volume is the one that must not be touched." >&2
  exit 1
fi

log "Done. ${WATCH_PATH} at ${used}% used."
