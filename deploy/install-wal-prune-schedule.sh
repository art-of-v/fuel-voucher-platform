#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Installs (or re-installs) the systemd timer that trims the WAL archive every six
# hours. Mirrors install-prune-schedule.sh. Idempotent: run it again after editing a
# unit and it just reloads and re-applies. It does NOT run a trim - it only
# schedules one, and it is zero-downtime.
#
#     sudo /root/FuelFlow/deploy/install-wal-prune-schedule.sh
#
# WHY A SEPARATE TIMER, AND NOT JUST THE BACKUP
#
# backup.sh already trims the archive. It does it at line 350 of a script that runs
# `set -euo pipefail`, after the dump, the base backup and the off-box upload - so
# a failure in any of those skips the trim that night. The archive is what the
# backup uploads, so the night the backup breaks is the night the archive stops
# being trimmed. The 8 GiB size cap that makes a runaway archive impossible is
# enforced by that same function, and it was therefore only as reliable as the job
# around it.
#
# This timer is the cap on its own schedule, with no database, no network and no
# age key - so it works on the night the backup does not.
#
# Afterwards, prove it worked:
#     systemctl list-timers fuelflow-wal-prune.timer
#     systemctl start fuelflow-wal-prune.service   # optional: run one trim now
#     journalctl -u fuelflow-wal-prune.service -n 50 --no-pager
# ------------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNIT_SRC="$SCRIPT_DIR/systemd"
UNIT_DST="/etc/systemd/system"

# The units hard-code /root/FuelFlow/deploy in ExecStart / WorkingDirectory. If this
# checkout lives elsewhere, copying them verbatim would schedule a trim pointing at
# a path that does not exist - and the failure would not show up until 00:17, when
# the timer fires and the unit fails. Fail loudly instead.
EXPECTED_DIR="/root/FuelFlow/deploy"

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ "${EUID:-$(id -u)}" -eq 0 ]] || fail "must run as root (try: sudo $0)"

[[ "$SCRIPT_DIR" == "$EXPECTED_DIR" ]] || fail \
  "expected to run from $EXPECTED_DIR but this is $SCRIPT_DIR.
       The systemd units reference $EXPECTED_DIR directly. Either check the repo out
       at /root/FuelFlow, or edit the paths in $UNIT_SRC/fuelflow-wal-prune*.{service,timer} first."

[[ -x "$SCRIPT_DIR/prune-wal-archive.sh" ]] || fail \
  "$SCRIPT_DIR/prune-wal-archive.sh is missing or not executable (chmod +x prune-wal-archive.sh)."

# The trim sources the shared helpers, so they have to be there and readable.
[[ -f "$SCRIPT_DIR/pitr-common.sh" ]] || fail \
  "$SCRIPT_DIR/pitr-common.sh is missing - the trim cannot run without it."

# The notifier is launched by systemd; make sure it can execute even if git dropped
# the bit.
if [[ -f "$SCRIPT_DIR/notify-prune-failure.sh" ]]; then
  chmod +x "$SCRIPT_DIR/notify-prune-failure.sh"
else
  fail "$SCRIPT_DIR/notify-prune-failure.sh is missing - the failure alert would not fire."
fi

[[ -d "$UNIT_SRC" ]] || fail "$UNIT_SRC not found - is this the deploy/ directory?"

UNITS=(
  fuelflow-wal-prune.service
  fuelflow-wal-prune.timer
)

echo "Installing systemd units into $UNIT_DST ..."
for unit in "${UNITS[@]}"; do
  [[ -f "$UNIT_SRC/$unit" ]] || fail "missing unit file: $UNIT_SRC/$unit"
  # 0644 root:root - unit files must not be group/world writable or systemd warns.
  install -m 0644 -o root -g root "$UNIT_SRC/$unit" "$UNIT_DST/$unit"
  echo "  installed $unit"
done

echo "Reloading systemd ..."
systemctl daemon-reload

# Enable + start the TIMER (not the service - the service is triggered by the timer).
echo "Enabling and starting fuelflow-wal-prune.timer ..."
systemctl enable --now fuelflow-wal-prune.timer

echo
echo "Done. Next scheduled run:"
systemctl list-timers fuelflow-wal-prune.timer --no-pager || true

echo
echo "To run one trim right now and watch it:"
echo "    systemctl start fuelflow-wal-prune.service && journalctl -fu fuelflow-wal-prune.service"

echo
echo "Note: the nightly backup still trims the archive too. That is deliberate - the"
echo "backup's trim frees the disk BEFORE it writes the base backup, and this timer's"
echo "trim is the guarantee that the bound holds even when the backup fails."
