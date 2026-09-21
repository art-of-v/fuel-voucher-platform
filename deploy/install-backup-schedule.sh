#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Installs (or re-installs) the systemd timer that runs the nightly backup.
#
# Run this ONCE on the server, as root, after pulling a checkout that contains
# deploy/systemd/. It is idempotent: run it again after editing a unit and it
# just reloads and re-applies. It does NOT run a backup — it only schedules one.
#
#     sudo /root/FuelFlow/deploy/install-backup-schedule.sh
#
# Afterwards, prove it worked:
#     systemctl list-timers fuelflow-backup.timer
#     systemctl start fuelflow-backup.service   # optional: run one backup now
#     journalctl -u fuelflow-backup.service -n 50 --no-pager
#
# WHY A SCRIPT AND NOT `crontab -e`
# The schedule used to be a crontab line typed on the box and recorded nowhere.
# This puts it under version control in deploy/systemd/ and makes installing it a
# repeatable, reviewable step of a rebuild instead of tribal knowledge.
# ------------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNIT_SRC="$SCRIPT_DIR/systemd"
UNIT_DST="/etc/systemd/system"

# The units hard-code /root/FuelFlow/deploy in ExecStart / WorkingDirectory. If this
# checkout lives somewhere else, copying them verbatim would schedule a backup that
# points at a path that does not exist — the worst kind of failure, because the timer
# looks healthy until 03:20. Fail loudly instead.
EXPECTED_DIR="/root/FuelFlow/deploy"

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ "${EUID:-$(id -u)}" -eq 0 ]] || fail "must run as root (try: sudo $0)"

[[ "$SCRIPT_DIR" == "$EXPECTED_DIR" ]] || fail \
  "expected to run from $EXPECTED_DIR but this is $SCRIPT_DIR.
       The systemd units reference $EXPECTED_DIR directly. Either check the repo out
       at /root/FuelFlow, or edit the paths in $UNIT_SRC/*.service and *.timer first."

[[ -x "$SCRIPT_DIR/backup.sh" ]] || fail \
  "$SCRIPT_DIR/backup.sh is missing or not executable (chmod +x backup.sh)."

[[ -f "$SCRIPT_DIR/notify-backup-failure.sh" ]] || fail \
  "$SCRIPT_DIR/notify-backup-failure.sh is missing — the failure alert would not fire."
# The notifier is launched by systemd; make sure it can execute even if git dropped the bit.
chmod +x "$SCRIPT_DIR/notify-backup-failure.sh"

[[ -d "$UNIT_SRC" ]] || fail "$UNIT_SRC not found — is this the deploy/ directory?"

UNITS=(
  fuelflow-backup.service
  fuelflow-backup.timer
  fuelflow-backup-alert@.service
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
# --now starts it immediately so we don't wait until the next boot for it to arm.
echo "Enabling and starting fuelflow-backup.timer ..."
systemctl enable --now fuelflow-backup.timer

echo
echo "Done. Next scheduled run:"
systemctl list-timers fuelflow-backup.timer --no-pager || true

echo
echo "To run one backup right now and watch it:"
echo "    systemctl start fuelflow-backup.service && journalctl -fu fuelflow-backup.service"
