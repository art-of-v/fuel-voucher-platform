#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Installs (or re-installs) the systemd timer that samples disk pressure every 15 minutes.
# Mirrors install-prune-schedule.sh and install-backup-schedule.sh. Idempotent: run it again
# after editing a unit and it just reloads and re-applies. It does NOT sample the disk - it only
# schedules the check, and it is zero-downtime.
#
#     sudo /root/FuelFlow/deploy/install-disk-check-schedule.sh
#
# Afterwards, prove it worked:
#     systemctl list-timers fuelflow-disk-check.timer
#     systemctl start fuelflow-disk-check.service      # one sample now; the journal says what it saw
#     journalctl -u fuelflow-disk-check.service -n 20 --no-pager
#
# The check only TELLS A HUMAN when the level changes (ok -> warn -> critical -> ok), so a healthy
# box is silent by design. To see it working without filling the disk, force a level once:
#     DISK_WARN_THRESHOLD=1 DISK_CRITICAL_THRESHOLD=2 ./disk-check.sh
# which exits 1 and writes an alert (warning, or critical on a box already above 2%) to the journal.
# Nothing is sent to Telegram. A forced run leaves its level behind, so reset afterwards:
#     rm -f /var/lib/fuelflow/disk-check.state
# ------------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNIT_SRC="$SCRIPT_DIR/systemd"
UNIT_DST="/etc/systemd/system"

# The units hard-code /root/FuelFlow/deploy in ExecStart / WorkingDirectory. If this checkout lives
# elsewhere, copying them verbatim would schedule a check pointing at a path that does not exist -
# the worst kind of failure, because the timer looks healthy until it fires. Fail loudly instead.
EXPECTED_DIR="/root/FuelFlow/deploy"
STATE_DIR="/var/lib/fuelflow"

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ "${EUID:-$(id -u)}" -eq 0 ]] || fail "must run as root (try: sudo $0)"

[[ "$SCRIPT_DIR" == "$EXPECTED_DIR" ]] || fail \
  "expected to run from $EXPECTED_DIR but this is $SCRIPT_DIR.
       The systemd units reference $EXPECTED_DIR directly. Either check the repo out at
       /root/FuelFlow, or edit the paths in $UNIT_SRC/fuelflow-disk-check*.{service,timer} first."

[[ -f "$SCRIPT_DIR/disk-check.sh" ]] || fail \
  "$SCRIPT_DIR/disk-check.sh is missing (chmod +x disk-check.sh)."
[[ -x "$SCRIPT_DIR/disk-check.sh" ]] || fail \
  "$SCRIPT_DIR/disk-check.sh is not executable. Fix with: chmod +x $SCRIPT_DIR/disk-check.sh"

[[ -f "$SCRIPT_DIR/notify-disk-check.sh" ]] || fail \
  "$SCRIPT_DIR/notify-disk-check.sh is missing - the alert would not fire."
# The notifier is launched by systemd; make sure it can execute even if git dropped the bit.
chmod +x "$SCRIPT_DIR/notify-disk-check.sh"

[[ -d "$UNIT_SRC" ]] || fail "$UNIT_SRC not found - is this the deploy/ directory?"

UNITS=(
  fuelflow-disk-check.service
  fuelflow-disk-check.timer
  fuelflow-disk-check-alert@.service
)

echo "Installing systemd units into $UNIT_DST ..."
for unit in "${UNITS[@]}"; do
  [[ -f "$UNIT_SRC/$unit" ]] || fail "missing unit file: $UNIT_SRC/$unit"
  # 0644 root:root - unit files must not be group/world writable or systemd warns.
  install -m 0644 -o root -g root "$UNIT_SRC/$unit" "$UNIT_DST/$unit"
  echo "  installed $unit"
done

# The check remembers the last level it reported so a disk that stays high is announced once rather
# than every 15 minutes. Losing that file costs notification spam, not correctness, so this is a
# warning rather than a failure - but it should be fixed, because a box that re-pages on every tick
# trains a human to ignore the page.
echo "Preparing the level state directory ..."
if install -d -m 0755 -o root -g root "$STATE_DIR"; then
  echo "  $STATE_DIR ready"
else
  echo "WARNING: could not create $STATE_DIR - the check will re-alert on every run until it exists." >&2
fi

echo "Reloading systemd ..."
systemctl daemon-reload

# Enable + start the TIMER (not the service - the service is triggered by the timer).
echo "Enabling and starting fuelflow-disk-check.timer ..."
systemctl enable --now fuelflow-disk-check.timer

echo
echo "Done. Next scheduled run:"
systemctl list-timers fuelflow-disk-check.timer --no-pager || true

echo
echo "To sample once right now and read what it saw:"
echo "    systemctl start fuelflow-disk-check.service && journalctl -u fuelflow-disk-check.service -n 20 --no-pager"
echo
echo "Silence is the healthy state. To prove the alerting path end to end without filling the"
echo "disk, lower the thresholds for one run (exits 1, journal only, no Telegram):"
echo "    cd $EXPECTED_DIR && DISK_WARN_THRESHOLD=1 DISK_CRITICAL_THRESHOLD=2 ./disk-check.sh"
echo "Reset afterwards with: rm -f $STATE_DIR/disk-check.state"