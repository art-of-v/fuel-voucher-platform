#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Operator actions for the off-box WAL shipper on the production box.
#
#     bash /root/FuelFlow/deploy/wal-ship-ops.sh <command>
#
# WHY THIS EXISTS
# The shipper, its timer and the host-loss drill were written and reviewed, but for a
# long time the timer was never INSTALLED on the box, and no drill ever wrote to the
# real bucket. Both halves of "the WAL leaves the box continuously" were therefore
# beliefs: a script that was right on a CI runner, and a box that was not running it.
#
# This script is the one place that does the box-side work, so the evidence of a run is
# a transcript rather than a recollection:
#
#   preflight    what is on the box, what is configured, what is already off-box
#   install      install + enable + start fueflow-wal-ship.timer
#   ship-once    run one shipment by hand and show what it did
#   lag          how old the newest off-box segment is, right now
#   verify       is every off-box segment WHOLE, or is one of them half-written
#   alert-drill  break the remote on purpose and prove the alert fires
#   drill        the real-R2 host-loss drill (real bucket, isolated prefix)
#   report       everything above, in one transcript
#   all          preflight -> install -> ship-once -> lag -> verify -> alert-drill -> drill -> report
#
# It runs on the box through the self-hosted GitHub Actions runner (see
# .github/workflows/wal-ship-ops.yml), because SSH into the box is firewalled and the
# runner is the only channel that reaches it. Every command is idempotent and can be
# run on its own; `all` is just the sequence.
#
# NOTHING HERE DELETES PRODUCTION DATA.
#   * the alert drill points the shipper at a remote that does not exist, by handing the
#     service an environment file for the length of one run - and the run after it must
#     succeed, which is the proof that the failure was injected and not pre-existing;
#   * the drill writes under a /drill prefix in the bucket and deletes that prefix
#     afterwards;
#   * the WAL switch closes the current segment early and writes no data.
# ------------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="${ENV_FILE:-$SCRIPT_DIR/.env.production}"
WAL_ARCHIVE_DIR="${WAL_ARCHIVE_DIR:-/root/fuelflow-wal}"
WAL_SHIP_STATE_DIR="${WAL_SHIP_STATE_DIR:-/var/lib/fuelflow}"
REPORT_FILE="${WAL_OPS_REPORT:-$WAL_SHIP_STATE_DIR/wal-ship-ops-$(date -u +%Y%m%dT%H%M%SZ).txt}"
RCLONE="${RCLONE_BIN:-rclone}"
TIMER="fuelflow-wal-ship.timer"
SERVICE="fuelflow-wal-ship.service"
ALERT_UNIT="fuelflow-wal-ship-alert@${SERVICE}.service"
ALERT_TAG="fuelflow-wal-ship-alert"
PG_CONTAINER="${PG_CONTAINER:-fuelflow-postgres}"
# The gate from the backup policy: a finished segment must be off-box within 15 minutes
# of PostgreSQL finishing it.
SHIP_LAG_GATE_SECONDS="${WAL_OPS_SHIP_LAG_GATE_SECONDS:-900}"
FORCE_SWITCH="${WAL_OPS_FORCE_SWITCH:-0}"
DRILL_KEEP="${DRILL_KEEP:-0}"
DRILL_REMOTE_OVERRIDE="${DRILL_REMOTE:-}"

# shellcheck source=wal-offbox.sh
source "$SCRIPT_DIR/wal-offbox.sh"

BACKUP_REMOTE=""
REMOTE_WAL=""
POSTGRES_USER_NAME=""
POSTGRES_DB_NAME=""

usage() {
  sed -n '2,/^set -euo pipefail$/{/^set -euo pipefail$/d;p}' "$0" | sed 's/^# \{0,1\}//'
  exit 1
}

section() {
  echo
  echo "==============================================================================="
  echo "$1"
  echo "==============================================================================="
}

say() { printf '%s\n' "$*"; }

# The env is PARSED, not sourced (see load-env.sh): a secret containing a space or a
# shell metacharacter must not be able to change what this run does.
load_box_env() {
  if [[ -f "$ENV_FILE" ]]; then
    # shellcheck source=load-env.sh
    source "$SCRIPT_DIR/load-env.sh"
    load_env "$ENV_FILE" || true
  fi
  BACKUP_REMOTE="${BACKUP_REMOTE:-}"
  if [[ -n "$BACKUP_REMOTE" ]]; then
    REMOTE_WAL="$(wal_remote_wal_dir "$BACKUP_REMOTE")"
  fi
  POSTGRES_USER_NAME="${POSTGRES_USER:-fuelflow}"
  POSTGRES_DB_NAME="${POSTGRES_DB:-fuelflow}"
}

# --- preflight ------------------------------------------------------------------------
cmd_preflight() {
  section "Preflight"
  local problems=0
  say "  host       : $(hostname 2>/dev/null || echo unknown-host)"
  say "  time       : $(date -Is)"
  say "  script dir : $SCRIPT_DIR"
  say "  archive dir: $WAL_ARCHIVE_DIR"
  say "  state dir  : $WAL_SHIP_STATE_DIR"
  say ""

  local cmd
  for cmd in systemctl rclone age age-keygen docker; do
    if command -v "$cmd" >/dev/null 2>&1; then
      printf '  OK    %-14s %s\n' "$cmd" "$(command -v "$cmd")"
    else
      printf '  MISS  %-14s (not installed)\n' "$cmd"
      problems=$((problems + 1))
    fi
  done

  if [[ -f "$ENV_FILE" ]]; then
    printf '  OK    %-14s %s\n' "env file" "$ENV_FILE"
  else
    printf '  MISS  %-14s %s is missing\n' "env file" "$ENV_FILE"
    problems=$((problems + 1))
  fi

  if [[ -n "$BACKUP_REMOTE" ]]; then
    printf '  OK    %-14s %s\n' "remote" "$BACKUP_REMOTE"
  else
    printf '  MISS  %-14s BACKUP_REMOTE is not set in %s\n' "remote" "$ENV_FILE"
    problems=$((problems + 1))
  fi

  if [[ -n "${BACKUP_AGE_RECIPIENT:-}" ]]; then
    printf '  OK    %-14s %s... (public key only)\n' "recipient" "${BACKUP_AGE_RECIPIENT:0:24}"
  else
    printf '  MISS  %-14s BACKUP_AGE_RECIPIENT is not set in %s\n' "recipient" "$ENV_FILE"
    problems=$((problems + 1))
  fi

  if [[ -d "$WAL_ARCHIVE_DIR" ]]; then
    local archived
    archived="$(find "$WAL_ARCHIVE_DIR" -maxdepth 1 -name '0000000*' -type f 2>/dev/null | wc -l | tr -d ' ')"
    printf '  OK    %-14s %s (%s segment(s))\n' "archive" "$WAL_ARCHIVE_DIR" "$archived"
  else
    printf '  MISS  %-14s %s does not exist - run deploy/enable-pitr.sh\n' "archive" "$WAL_ARCHIVE_DIR"
    problems=$((problems + 1))
  fi

  if [[ -n "$BACKUP_REMOTE" ]]; then
    if "$RCLONE" lsf "$REMOTE_WAL" --files-only >/dev/null 2>&1; then
      printf '  OK    %-14s %s is reachable\n' "bucket" "$REMOTE_WAL"
    else
      printf '  MISS  %-14s %s is not reachable\n' "bucket" "$REMOTE_WAL"
      problems=$((problems + 1))
    fi
  fi

  # How the remote is configured. This is not decoration: the alert drill has to break
  # the remote without touching whatever the configuration is, and the answer to "where
  # is rclone's config?" is different on every box.
  local rclone_version rclone_config
  rclone_version="$("$RCLONE" version 2>/dev/null | head -1 || true)"
  rclone_config="$("$RCLONE" config file 2>/dev/null || true)"
  say "  rclone:"
  say "    version     : ${rclone_version:-unknown}"
  if [[ -n "$rclone_config" ]]; then
    if [[ -f "$rclone_config" ]]; then
      say "    config file : $rclone_config (present)"
    else
      say "    config file : $rclone_config (not there - the remote must be configured another way)"
    fi
  else
    say "    config file : rclone reported none"
  fi
  if env | grep -q '^RCLONE_CONFIG'; then
    say "    environment : RCLONE_CONFIG* variables are set"
  else
    say "    environment : no RCLONE_CONFIG* variables"
  fi

  if docker ps --format '{{.Names}}' 2>/dev/null | grep -qx "$PG_CONTAINER"; then
    printf '  OK    %-14s %s is running\n' "database" "$PG_CONTAINER"
  else
    printf '  MISS  %-14s %s is not running\n' "database" "$PG_CONTAINER"
    problems=$((problems + 1))
  fi

  # The timer is not installed yet on the box - that is what the install step is for, so
  # it is reported, not counted as a problem.
  if systemctl cat "$TIMER" >/dev/null 2>&1; then
    printf '  INFO  %-14s installed (%s)\n' "timer" "$(systemctl is-enabled "$TIMER" 2>/dev/null || echo unknown)"
  else
    printf '  INFO  %-14s not installed yet - the install step installs it\n' "timer"
  fi

  say ""
  if (( problems > 0 )); then
    say "  $problems problem(s) found." >&2
    return 1
  fi
  say "  Preflight clean."
}

# --- install --------------------------------------------------------------------------
cmd_install() {
  section "Install the timer"
  bash "$SCRIPT_DIR/install-wal-ship-schedule.sh"
  say ""
  say "  is-enabled: $(systemctl is-enabled "$TIMER" 2>/dev/null || echo unknown)"
  say "  is-active : $(systemctl is-active "$TIMER" 2>/dev/null || echo unknown)"
  systemctl list-timers "$TIMER" --no-pager || true
}

# --- one shipment ---------------------------------------------------------------------
cmd_ship_once() {
  section "One shipment, by hand"
  if ! systemctl start "$SERVICE"; then
    say "ERROR: the shipment failed. The journal below says why." >&2
    journalctl -u "$SERVICE" -n 40 --no-pager || true
    return 1
  fi
  journalctl -u "$SERVICE" -n 40 --no-pager || true
  say ""
  say "  the status object ($REMOTE_WAL/status.txt):"
  "$RCLONE" cat "$REMOTE_WAL/status.txt" 2>/dev/null | sed 's/^/    /' || say "    <no status object>"
  say ""
  say "  the newest objects in $REMOTE_WAL:"
  "$RCLONE" lsf "$REMOTE_WAL" --files-only 2>/dev/null | sort | tail -20 | sed 's/^/    /' || true
}

# --- the lag --------------------------------------------------------------------------
cmd_lag() {
  section "How old is the newest off-box segment?"

  if [[ "$FORCE_SWITCH" == "1" ]]; then
    say "  Forcing a WAL switch, so the measurement is not 'the database was quiet' ..."
    docker exec "$PG_CONTAINER" psql -U "$POSTGRES_USER_NAME" -d "$POSTGRES_DB_NAME" \
      -Atc 'SELECT pg_switch_wal()' 2>/dev/null || say "  (the switch did not report; the archiver may still catch up)"
    sleep 5
  fi

  say "  Shipping what is on the box ..."
  systemctl start "$SERVICE" || {
    say "ERROR: the shipment failed; see the journal." >&2
    journalctl -u "$SERVICE" -n 30 --no-pager >&2 || true
    return 1
  }

  local status ship_lag
  status="$("$RCLONE" cat "$REMOTE_WAL/status.txt" 2>/dev/null || true)"
  ship_lag="$(sed -n 's/^last_upload_lag_seconds=//p' <<<"$status" | tail -1)"
  say ""
  say "  the status object:"
  printf '%s\n' "$status" | sed 's/^/    /'
  say ""

  # The newest segment on the box.
  local newest="" f name
  for f in "$WAL_ARCHIVE_DIR"/0000000*; do
    [[ -f "$f" ]] || continue
    name="$(basename "$f")"
    wal_object_is_segment "$name" || continue
    if [[ -z "$newest" || "$name" > "$newest" ]]; then
      newest="$name"
    fi
  done
  if [[ -z "$newest" ]]; then
    say "  There is no finished segment in $WAL_ARCHIVE_DIR, so there is nothing to measure." >&2
    return 1
  fi
  say "  newest segment on the box : $newest ($(stat -c %Y "$WAL_ARCHIVE_DIR/$newest") epoch)"

  # The newest segment that is off-box.
  local newest_off="" object
  while IFS= read -r object; do
    [[ -n "$object" ]] || continue
    [[ "$object" == *.age ]] || continue
    name="${object%.age}"
    wal_object_is_segment "$name" || continue
    if [[ -z "$newest_off" || "$name" > "$newest_off" ]]; then
      newest_off="$name"
    fi
  done < <("$RCLONE" lsf "$REMOTE_WAL" --files-only 2>/dev/null || true)

  if [[ -z "$newest_off" ]]; then
    say "  nothing is off-box yet." >&2
    return 1
  fi
  say "  newest segment off-box    : $newest_off"
  if [[ -f "$WAL_ARCHIVE_DIR/$newest_off" ]]; then
    local off_age
    off_age=$(( $(date +%s) - $(stat -c %Y "$WAL_ARCHIVE_DIR/$newest_off") ))
    say "  its age                   : ${off_age}s (this is the host-loss recovery point)"
  else
    say "  its age                   : unknown (the segment is not in the local archive any more)"
  fi

  if [[ -z "$ship_lag" ]]; then
    say "  the shipper reported no lag, so the archived-to-off-box time is unknown." >&2
    return 1
  fi
  say "  archived-to-off-box lag   : ${ship_lag}s (the gate is ${SHIP_LAG_GATE_SECONDS}s)"
  if (( ship_lag > SHIP_LAG_GATE_SECONDS )); then
    say "  => OUTSIDE the gate. The copy that survives the host is too old." >&2
    return 1
  fi
  say "  => within the gate."
}

# --- the alert drill ------------------------------------------------------------------
cmd_alert_drill() {
  section "Alert drill: break the remote on purpose"

  if [[ -z "${BACKUP_AGE_RECIPIENT:-}" ]]; then
    say "ERROR: BACKUP_AGE_RECIPIENT is not set, so the shipper fails before it ever" >&2
    say "       reaches the remote and the alert would prove the wrong thing." >&2
    return 1
  fi

  # The failure is injected by pointing the shipper at a remote that does not exist,
  # through an environment file the service picks up. Nothing on the box is edited: the
  # file holds the two settings the shipper needs, lives for one run, and systemd's own
  # environment is cleared again right after.
  #
  # Why not "move rclone's config aside" (what this drill did first): where rclone keeps
  # its config is not the same on every box - the first run of this drill found no config
  # file at all. An unknown remote name fails the same way whatever the configuration is,
  # and it cannot create anything, because no request reaches the network.
  local fake_env="$WAL_SHIP_STATE_DIR/wal-ship-ops-alert-drill.env"
  local fake_remote="wal-ship-ops-alert-drill:no-such-bucket"
  # The umask is confined to a subshell on purpose. Setting it here leaked into the rest
  # of the process - and in the `all` campaign the drill runs after this step, so the
  # restore it makes fetched its WAL files with mode 600, the restored container's
  # postgres user could not read them ("cp: can't stat ... Permission denied"), and
  # recovery ended at the base backup's own WAL. One `umask` cost the drill its proof.
  ( umask 077
    printf 'BACKUP_REMOTE=%s\n' "$fake_remote" > "$fake_env"
    printf 'BACKUP_AGE_RECIPIENT=%s\n' "$BACKUP_AGE_RECIPIENT" >> "$fake_env"
  )
  chmod 600 "$fake_env"
  say "  the shipper is pointed at $fake_remote (a remote that does not exist)"
  say ""

  end_injection() {
    # The path is spelled out here, not read from a local: an EXIT trap runs after the
    # function that set the local has returned, and its frame is gone by then.
    systemctl unset-environment ENV_FILE >/dev/null 2>&1 || true
    rm -f "$WAL_SHIP_STATE_DIR/wal-ship-ops-alert-drill.env"
  }
  trap end_injection EXIT
  systemctl set-environment ENV_FILE="$fake_env"

  local started rc
  started="$(date '+%Y-%m-%d %H:%M:%S')"
  rc=0
  systemctl start "$SERVICE" || rc=$?
  say "  systemctl start $SERVICE -> exit $rc"
  if (( rc == 0 )); then
    say "ERROR: the shipment SUCCEEDED with an impossible remote, so this drill" >&2
    say "       did not test anything." >&2
    return 1
  fi
  say "  the shipment failed, as it should."
  sleep 5

  say ""
  say "  the alert unit's journal:"
  journalctl -u "$ALERT_UNIT" --since "$started" --no-pager 2>/dev/null | sed 's/^/    /' || true

  local alert_log
  alert_log="$(journalctl -t "$ALERT_TAG" --since "$started" --no-pager -o cat 2>/dev/null || true)"
  say ""
  say "  the alert's own log:"
  printf '%s\n' "$alert_log" | sed 's/^/    /'

  if ! grep -q "WAL OFF-BOX SHIPPING FAILED" <<<"$alert_log"; then
    say "ERROR: the alert did not fire. The journal above is the evidence." >&2
    return 1
  fi
  say ""
  say "  the alert fired."

  if grep -q "alert delivered to Telegram" <<<"$alert_log"; then
    say "  and it was delivered to Telegram."
  elif grep -q "Telegram delivery failed" <<<"$alert_log"; then
    say "  but the Telegram delivery FAILED: the alert is in the journal only." >&2
    return 1
  else
    say "  but Telegram is not configured, so the alert is in the journal only." >&2
    return 1
  fi

  say ""
  say "  the injection is over; the next shipment must succeed:"
  trap - EXIT
  end_injection
  rc=0
  systemctl start "$SERVICE" || rc=$?
  if (( rc != 0 )); then
    say "ERROR: the shipment still fails after the injection was removed, so the" >&2
    say "       fault is not the injected one." >&2
    journalctl -u "$SERVICE" -n 30 --no-pager >&2 || true
    return 1
  fi
  say "  the shipment succeeded again: the failure was injected, and it is over."
}

# --- the real-R2 host-loss drill ------------------------------------------------------
cmd_drill() {
  section "The real-R2 host-loss drill"

  local remote="$DRILL_REMOTE_OVERRIDE"
  if [[ -z "$remote" ]]; then
    remote="$BACKUP_REMOTE/drill/695-$(date -u +%Y%m%dT%H%M%SZ)"
  fi
  say "  drill prefix : $remote"
  say "  the drill generates its own age keypair; the production private key is not here."
  say ""

  DRILL_REMOTE="$remote" DRILL_KEEP="$DRILL_KEEP" bash "$SCRIPT_DIR/drill-wal-offbox.sh"
}

# --- the verification -----------------------------------------------------------------
# Is the off-box copy WHOLE? A segment that was encrypted while the archiver was still
# copying it decrypts, looks right and is never uploaded again - and it stops a restore
# replaying at exactly that point, silently. The size is the only way to tell from the
# outside, so it is checked here, for every segment on the box.
cmd_verify() {
  section "Verify the off-box chain"

  local problems=0 checked=0 short=0 missing=0 unknown=0 f name local_size remote_size
  # The NAMES are the source of truth for "is it off-box"; the sizes are only needed to
  # tell a whole object from a half-written one. If the sizes cannot be read, the check
  # says so and says nothing else - a broken listing must never be reported as a
  # broken chain.
  declare -A remote_names=()
  local object
  while IFS= read -r object; do
    [[ -n "$object" ]] || continue
    remote_names["$object"]=1
  done < <("$RCLONE" lsf "$REMOTE_WAL" --files-only 2>/dev/null || true)

  declare -A remote_sizes=()
  while read -r rname rsize; do
    [[ -n "$rname" && -n "$rsize" ]] || continue
    remote_sizes["$rname"]="$rsize"
  done < <(wal_remote_object_sizes "$REMOTE_WAL" || true)

  say "  the off-box objects: ${#remote_names[@]}"
  if (( ${#remote_sizes[@]} == 0 )); then
    say "  NOTE: the object sizes could not be read, so only presence is checked." >&2
  fi
  say "  the on-box segments are compared with them, one by one:"

  for f in "$WAL_ARCHIVE_DIR"/0000000*; do
    [[ -f "$f" ]] || continue
    name="$(basename "$f")"
    wal_object_is_segment "$name" || continue
    local_size="$(wal_segment_size "$f")"
    checked=$((checked + 1))
    if [[ -z "${remote_names[$name.age]:-}" ]]; then
      say "    $name  on box ${local_size} bytes  off-box: MISSING"
      missing=$((missing + 1))
      continue
    fi
    remote_size="${remote_sizes[$name.age]:-}"
    if [[ -z "$remote_size" ]]; then
      say "    $name  on box ${local_size} bytes  off-box: present (size unknown)"
      unknown=$((unknown + 1))
      continue
    fi
    if (( remote_size < local_size )); then
      say "    $name  on box ${local_size} bytes  off-box: ${remote_size} bytes  <-- TRUNCATED"
      short=$((short + 1))
      continue
    fi
    say "    $name  on box ${local_size} bytes  off-box: ${remote_size} bytes  ok"
  done

  say ""
  if (( short > 0 )); then
    say "ERROR: $short off-box segment(s) are SHORTER than the segment on the box." >&2
    say "       They were uploaded half-written, and a restore stops replaying at the" >&2
    say "       first of them without saying why. The shipper re-uploads them on its" >&2
    say "       next tick; run 'ship-once' now if you want it done immediately." >&2
    problems=$((problems + 1))
  fi
  if (( missing > 0 )); then
    say "ERROR: $missing segment(s) on the box are not off-box at all." >&2
    problems=$((problems + 1))
  fi
  if (( problems == 0 )); then
    say "  $checked segment(s) checked; every off-box copy is present"
    if (( unknown > 0 )); then
      say "  and $unknown of them could not be size-checked (the sizes were not readable)."
    else
      say "  and whole."
    fi
    return 0
  fi
  return 1
}

# --- the report -----------------------------------------------------------------------
cmd_report() {
  section "Report"
  say "  timer:"
  systemctl list-timers "$TIMER" --no-pager 2>/dev/null | sed 's/^/    /' || true
  say "    is-enabled: $(systemctl is-enabled "$TIMER" 2>/dev/null || echo unknown)"
  say "    is-active : $(systemctl is-active "$TIMER" 2>/dev/null || echo unknown)"

  say ""
  say "  the shipper's journal:"
  journalctl -u "$SERVICE" -n 30 --no-pager 2>/dev/null | sed 's/^/    /' || true

  say ""
  say "  the alert unit's journal:"
  journalctl -u "$ALERT_UNIT" -n 20 --no-pager 2>/dev/null | sed 's/^/    /' || true

  say ""
  say "  the status object:"
  "$RCLONE" cat "$REMOTE_WAL/status.txt" 2>/dev/null | sed 's/^/    /' || say "    <no status object>"

  say ""
  say "  the newest objects in $REMOTE_WAL:"
  "$RCLONE" lsf "$REMOTE_WAL" --files-only 2>/dev/null | sort | tail -20 | sed 's/^/    /' || true

  say ""
  say "  the newest files in $WAL_ARCHIVE_DIR:"
  ls -l --time-style=long-iso "$WAL_ARCHIVE_DIR" 2>/dev/null | tail -10 | sed 's/^/    /' || true
}

# One map, used by both the dispatcher and the campaign, so a command name is spelled in
# exactly one place. The first version had cmd_all call "cmd_ship-once" and "cmd_alert-drill",
# which are not function names: the whole campaign died on its third step.
ops_function() {
  case "$1" in
    preflight)   echo cmd_preflight ;;
    install)     echo cmd_install ;;
    ship-once)   echo cmd_ship_once ;;
    lag)         echo cmd_lag ;;
    alert-drill) echo cmd_alert_drill ;;
    drill)       echo cmd_drill ;;
    verify)      echo cmd_verify ;;
    report)      echo cmd_report ;;
    all)         echo cmd_all ;;
    *)           return 1 ;;
  esac
}

# --- the whole campaign ---------------------------------------------------------------
cmd_all() {
  local rc=0 step fn
  for step in preflight install ship-once lag verify alert-drill drill; do
    rc=0
    fn="$(ops_function "$step")"
    "$fn" || rc=$?
    if (( rc != 0 )); then
      say "ERROR: the '$step' step failed; stopping here." >&2
      break
    fi
  done
  cmd_report || true
  return "$rc"
}

main() {
  load_box_env
  case "${1:-help}" in
    help|-h|--help|"") usage ;;
  esac
  local fn
  fn="$(ops_function "$1")" || { echo "unknown command: $1" >&2; usage; }
  "$fn"
}

mkdir -p "$(dirname "$REPORT_FILE")"
# The transcript is the evidence, so it is written whatever happens - including when a
# step fails and the run stops.
set +e
main "$@" 2>&1 | tee -a "$REPORT_FILE"
rc="${PIPESTATUS[0]}"
set -e
echo
echo "The transcript of this run is in $REPORT_FILE"
exit "$rc"
