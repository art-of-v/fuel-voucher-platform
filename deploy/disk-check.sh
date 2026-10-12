#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Disk pressure check - the early warning that the nightly GC does not give.
#
# WHY THIS EXISTS (planning #263, item 3)
# The nightly prune already alerts, but only when the disk is STILL at/above 85% AFTER it has
# reclaimed everything it can. Two gaps, both of which were true on the day the box filled:
#
#   1. Once a day. It samples / at 04:20 and nothing else. A disk that crosses the line at 06:00
#      is invisible for the next 22 hours, which is longer than it took to go from "fine" to
#      "Postgres cannot write postmaster.pid" on 2026-09-27.
#   2. Only after a failure. Nothing says "/ is at 80% and climbing" while there is still room to
#      react - the alert arrives at the same moment the outage does.
#
# So: sample every 15 minutes, alert on a band below the prune's own threshold, and stay silent
# while nothing changes.
#
# WHAT IT DELIBERATELY DOES NOT DO
#   - Reclaim anything. That is prune-docker.sh's job, and this runs against the same disk far
#     more often.
#   - Page on every sample. An alert that fires 96 times a day is an alert people mute, so the
#     level is compared against the last level reported and only a CHANGE is worth a message:
#     ok -> warn, warn -> critical, and back to ok again ("recovered"). A disk sitting at 91%
#     says it once, not every quarter of an hour for a week.
#
# WHY IT EXITS NON-ZERO TO TALK
# The notification path in this repo is systemd's OnFailure= hook (see notify-prune-failure.sh):
# the check has no Telegram credentials of its own and does not send anything. It exits non-zero
# when it has something to say, the unit fails, and fuelflow-disk-check-alert@ pushes the journal
# excerpt to the same Telegram group as everything else. Exit codes:
#   0 = healthy, and nothing new to say
#   1 = something to tell a human (warn / critical / recovered)
#   2 = the check itself is broken (cannot measure, or misconfigured) - also worth a human
#
# Every 15 min: installed as fuelflow-disk-check.timer by install-disk-check-schedule.sh
# One-off run:  cd /root/FuelFlow/deploy && ./disk-check.sh
# ------------------------------------------------------------------------------
set -uo pipefail

# The filesystem Docker's data root lives on - the same one the prune watches.
WATCH_PATH="${WATCH_PATH:-/}"
# Below DISK_CRITICAL_THRESHOLD, above the prune's own 85% failure mark, so the warning arrives
# while there is still room to do something about it.
DISK_WARN_THRESHOLD="${DISK_WARN_THRESHOLD:-80}"
DISK_CRITICAL_THRESHOLD="${DISK_CRITICAL_THRESHOLD:-90}"
# Last level reported. Survives reboots; that is the point - a box that was at 95% and came back
# at 95% must not re-announce itself every 15 minutes just because the box restarted.
STATE_FILE="${STATE_FILE:-/var/lib/fuelflow/disk-check.state}"

# Overridable so the test harness can drive every branch without a full filesystem or a real
# Postgres. Both default to the production commands.
DF_BIN="${DF_BIN:-df}"
PG_SIZE_CMD="${PG_SIZE_CMD:-docker exec fuelflow-postgres sh -c 'psql -U \"\$POSTGRES_USER\" -d \"\$POSTGRES_DB\" -tAc \"SELECT pg_database_size(current_database())\"'}"

log() { echo "[$(date -Is)] $*"; }
# The notifier reads the journal, so every line printed here ends up in the alert message. One
# print per line, through log - saying it twice would put the whole report into the Telegram message
# twice, and Telegram truncates at 4096 characters.
say() { log "$*"; }

# ── configuration ─────────────────────────────────────────────────────────────
# A misconfigured threshold would silently compare the wrong way round - `(( 80 >= 90 ))` is
# simply false, and a broken check that says "everything is fine" is the worst possible failure
# for a check whose entire job is noticing. Refuse to run instead.
if ! [[ "$DISK_WARN_THRESHOLD" =~ ^[0-9]+$ && "$DISK_CRITICAL_THRESHOLD" =~ ^[0-9]+$ ]] \
   || (( DISK_CRITICAL_THRESHOLD <= DISK_WARN_THRESHOLD )); then
  say "MISCONFIGURED: warn=${DISK_WARN_THRESHOLD}% critical=${DISK_CRITICAL_THRESHOLD}% - critical must be a higher number than warn"
  exit 2
fi

# ── measure ───────────────────────────────────────────────────────────────────
# `df -P` forces portable single-line output; column 5 is "Use%" like "83%".
pct="$("$DF_BIN" -P "$WATCH_PATH" 2>/dev/null | awk 'NR==2 { gsub(/%/,"",$5); print $5 }')"

if ! [[ "$pct" =~ ^[0-9]+$ ]]; then
  # Refusing to report "healthy" is the point. An alert about a broken check is worth more than
  # the silence a silent failure would produce.
  say "CANNOT MEASURE: ${DF_BIN} -P ${WATCH_PATH} did not yield a usage percentage (got: '${pct:-<empty>}'). Disk pressure is NOT being watched."
  exit 2
fi

# Context, never the trigger: which thing is filling the disk. The answer on this box is almost
# always the database (it is the one thing nobody prunes), and a bare "83%" leaves the operator to
# go and work that out during an incident. Best-effort by design - a stopped container must not
# stop the check from running.
pg_size="unavailable"
if bytes="$(bash -c "$PG_SIZE_CMD" 2>/dev/null | tr -dc '0-9')" && [[ -n "$bytes" ]]; then
  if command -v numfmt > /dev/null 2>&1; then
    pg_size="$(numfmt --to=iec --suffix=B "$bytes" 2>/dev/null || echo "${bytes} B")"
  else
    pg_size="${bytes} B"
  fi
fi

# ── classify ──────────────────────────────────────────────────────────────────
if (( pct >= DISK_CRITICAL_THRESHOLD )); then
  level="critical"
elif (( pct >= DISK_WARN_THRESHOLD )); then
  level="warn"
else
  level="ok"
fi

last="$(cat "$STATE_FILE" 2>/dev/null | tr -dc 'a-z' || true)"
[[ -n "$last" ]] || last="none"

# Persist the level so the next run has something to compare against. Best-effort by design: if
# the box is under enough pressure that this write fails, the alert still goes out this run - and
# the next run re-reports, which is the right failure mode. An operator getting a repeat is better
# than a disk that fills silently because a state file went missing.
persist() {
  mkdir -p "$(dirname "$STATE_FILE")" 2>/dev/null || true
  printf '%s\n' "$level" > "$STATE_FILE" 2>/dev/null \
    || say "NOTE: could not persist the last level to ${STATE_FILE}. This check will re-alert on every run until that path is writable."
}

# Nothing new to say: healthy, or still the same problem we already told a human about. Either way
# the right action is silence. A first-ever run on a healthy disk is silence too - "recovered from
# nothing" is not news.
if [[ "$level" == "$last" ]] || { [[ "$level" == "ok" ]] && [[ "$last" == "none" ]]; }; then
  [[ "$level" == "ok" ]] && persist   # write the baseline the next run compares against
  log "OK: ${WATCH_PATH} at ${pct}% (level ${level}, unchanged). Nothing to report."
  exit 0
fi

# ── report ────────────────────────────────────────────────────────────────────
persist

case "$level" in
  critical)
    say "🔴 CRITICAL: disk pressure on $(hostname 2>/dev/null || echo unknown-host)
${WATCH_PATH} at ${pct}% used (critical >= ${DISK_CRITICAL_THRESHOLD}%), up from '${last}'.
postgres database size: ${pg_size}

On 2026-09-27 this disk reached 100% and Postgres crash-looped with
'could not write lock file \"postmaster.pid\": No space left on device', taking the API down.
The nightly GC reclaims images and build cache only - it cannot reclaim the database, container
logs or Loki data, so this is the case that needs a human.

    journalctl -u fuelflow-disk-check.service -n 50 --no-pager
    df -h ${WATCH_PATH} && docker system df
    docker exec fuelflow-postgres psql -U \$POSTGRES_USER -d \$POSTGRES_DB -c \"SELECT pg_size_pretty(pg_database_size(current_database()));\""
    ;;
  warn)
    say "🟡 WARNING: disk pressure on $(hostname 2>/dev/null || echo unknown-host)
${WATCH_PATH} at ${pct}% used (warning >= ${DISK_WARN_THRESHOLD}%), up from '${last}'.
postgres database size: ${pg_size}

Not an outage yet - this is the headroom the nightly GC does not warn about. Check what is
growing before the nightly prune has to reclaim its way out of it:
    df -h ${WATCH_PATH} && docker system df
    docker exec fuelflow-postgres psql -U \$POSTGRES_USER -d \$POSTGRES_DB -c \"SELECT pg_size_pretty(pg_database_size(current_database()));\""
    ;;
  ok)
    # The disk came back down - usually the nightly GC, sometimes a human. Said out loud, because
    # an alert channel that never says "resolved" is one nobody trusts next time.
    say "🟢 RECOVERED: disk pressure on $(hostname 2>/dev/null || echo unknown-host)
${WATCH_PATH} back to ${pct}% used (below the ${DISK_WARN_THRESHOLD}% warning), down from '${last}'.
postgres database size: ${pg_size}"
    ;;
esac

exit 1