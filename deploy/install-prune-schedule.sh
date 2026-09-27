#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Installs (or re-installs) the systemd timer that runs the nightly Docker GC.
# Mirrors install-backup-schedule.sh. Idempotent: run it again after editing a unit and
# it just reloads and re-applies. It does NOT run a prune — it only schedules one, and it
# is zero-downtime (unlike applying daemon.json log rotation, which restarts Docker).
#
#     sudo /root/FuelFlow/deploy/install-prune-schedule.sh
#
# Afterwards, prove it worked:
#     systemctl list-timers fuelflow-prune.timer
#     systemctl start fuelflow-prune.service   # optional: run one GC now
#     journalctl -u fuelflow-prune.service -n 50 --no-pager
# ------------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNIT_SRC="$SCRIPT_DIR/systemd"
UNIT_DST="/etc/systemd/system"

# The units hard-code /root/FuelFlow/deploy in ExecStart / WorkingDirectory. If this
# checkout lives elsewhere, copying them verbatim would schedule a GC pointing at a path
# that does not exist — the worst kind of failure, because the timer looks healthy until
# 04:20. Fail loudly instead.
EXPECTED_DIR="/root/FuelFlow/deploy"

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ "${EUID:-$(id -u)}" -eq 0 ]] || fail "must run as root (try: sudo $0)"

[[ "$SCRIPT_DIR" == "$EXPECTED_DIR" ]] || fail \
  "expected to run from $EXPECTED_DIR but this is $SCRIPT_DIR.
       The systemd units reference $EXPECTED_DIR directly. Either check the repo out
       at /root/FuelFlow, or edit the paths in $UNIT_SRC/fuelflow-prune*.{service,timer} first."

[[ -x "$SCRIPT_DIR/prune-docker.sh" ]] || fail \
  "$SCRIPT_DIR/prune-docker.sh is missing or not executable (chmod +x prune-docker.sh)."

[[ -f "$SCRIPT_DIR/notify-prune-failure.sh" ]] || fail \
  "$SCRIPT_DIR/notify-prune-failure.sh is missing — the failure alert would not fire."
# The notifier is launched by systemd; make sure it can execute even if git dropped the bit.
chmod +x "$SCRIPT_DIR/notify-prune-failure.sh"

[[ -d "$UNIT_SRC" ]] || fail "$UNIT_SRC not found — is this the deploy/ directory?"

UNITS=(
  fuelflow-prune.service
  fuelflow-prune.timer
  fuelflow-prune-alert@.service
)

echo "Installing systemd units into $UNIT_DST ..."
for unit in "${UNITS[@]}"; do
  [[ -f "$UNIT_SRC/$unit" ]] || fail "missing unit file: $UNIT_SRC/$unit"
  # 0644 root:root — unit files must not be group/world writable or systemd warns.
  install -m 0644 -o root -g root "$UNIT_SRC/$unit" "$UNIT_DST/$unit"
  echo "  installed $unit"
done

echo "Reloading systemd ..."
systemctl daemon-reload

# Enable + start the TIMER (not the service — the service is triggered by the timer).
echo "Enabling and starting fuelflow-prune.timer ..."
systemctl enable --now fuelflow-prune.timer

echo
echo "Done. Next scheduled run:"
systemctl list-timers fuelflow-prune.timer --no-pager || true

echo
echo "To run one GC right now and watch it:"
echo "    systemctl start fuelflow-prune.service && journalctl -fu fuelflow-prune.service"
