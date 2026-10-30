#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Installs (or re-installs) the systemd timer that ships the WAL archive off-box every
# minute. Mirrors install-wal-prune-schedule.sh. Idempotent: run it again after editing a
# unit and it reloads and re-applies. It does NOT run a shipment - it only schedules one,
# and it is zero-downtime.
#
#     sudo /root/FuelFlow/deploy/install-wal-ship-schedule.sh
#
# WHY A SEPARATE TIMER
#
# The nightly backup ships the archive as one tarball, so the off-box copy is a day old
# while the on-box archive is minutes old. The timer is what closes that gap: it lists
# the remote, uploads the segments that are not there yet, and writes a status object.
# It is deliberately not inside backup.sh, because a failure anywhere in that long
# `set -euo pipefail` script would otherwise skip the shipping that night - the same
# mistake the WAL trim made before it got its own timer.
#
# Afterwards, prove it worked:
#     systemctl list-timers fuelflow-wal-ship.timer
#     systemctl start fuelflow-wal-ship.service
#     journalctl -u fuelflow-wal-ship.service -n 50 --no-pager
#     rclone lsf r2:fuelflow-backups/wal | tail
#     rclone cat r2:fuelflow-backups/wal/status.txt
# ------------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNIT_SRC="$SCRIPT_DIR/systemd"
UNIT_DST="/etc/systemd/system"

# The units hard-code /root/FuelFlow/deploy in ExecStart / WorkingDirectory. If this
# checkout lives elsewhere, copying them verbatim would schedule a shipment pointing at
# a path that does not exist - and the failure would not show up until the first tick.
# Fail loudly instead.
EXPECTED_DIR="/root/FuelFlow/deploy"

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ "${EUID:-$(id -u)}" -eq 0 ]] || fail "must run as root (try: sudo $0)"

[[ "$SCRIPT_DIR" == "$EXPECTED_DIR" ]] || fail \
  "expected to run from $EXPECTED_DIR but this is $SCRIPT_DIR.
       The systemd units reference $EXPECTED_DIR directly. Either check the repo out
       at /root/FuelFlow, or edit the paths in $UNIT_SRC/fuelflow-wal-ship*.{service,timer} first."

[[ -f "$SCRIPT_DIR/ship-wal-offbox.sh" ]] || fail \
  "$SCRIPT_DIR/ship-wal-offbox.sh is missing - the timer would have nothing to run."
[[ -f "$SCRIPT_DIR/wal-offbox.sh" ]] || fail \
  "$SCRIPT_DIR/wal-offbox.sh is missing - the shipper cannot encrypt or list the remote without it."

# The shipper and the notifier are launched by systemd; make sure they can execute even
# if git dropped the bit.
for f in ship-wal-offbox.sh notify-wal-ship-failure.sh; do
  if [[ ! -f "$SCRIPT_DIR/$f" ]]; then
    fail "$SCRIPT_DIR/$f is missing."
  fi
  chmod +x "$SCRIPT_DIR/$f"
done

command -v rclone >/dev/null 2>&1 || fail \
  "rclone is not installed. The shipper would fail every minute. Install it first."
command -v age >/dev/null 2>&1 || fail \
  "age is not installed. The shipper refuses to upload a plaintext WAL segment."

# The shipper reads the same .env.production the stack uses. Parse, don't source (see load-env.sh).
[[ -f "$SCRIPT_DIR/.env.production" ]] || fail \
  "$SCRIPT_DIR/.env.production is missing. Copy .env.production.example to .env.production and fill it in first."
# shellcheck source=load-env.sh
source "$SCRIPT_DIR/load-env.sh"
load_env "$SCRIPT_DIR/.env.production"

[[ -n "${BACKUP_REMOTE:-}" ]] || fail \
  "BACKUP_REMOTE is not set in $SCRIPT_DIR/.env.production (e.g. BACKUP_REMOTE=r2:fuelflow-backups)."
[[ -n "${BACKUP_AGE_RECIPIENT:-}" ]] || fail \
  "BACKUP_AGE_RECIPIENT is not set in $SCRIPT_DIR/.env.production. The shipper cannot encrypt without it."

WAL_ARCHIVE_DIR="${WAL_ARCHIVE_DIR:-/root/fuelflow-wal}"
[[ -d "$WAL_ARCHIVE_DIR" ]] || fail \
  "$WAL_ARCHIVE_DIR does not exist. Run deploy/enable-pitr.sh first, so there is an archive to ship."

[[ -d "$UNIT_SRC" ]] || fail "$UNIT_SRC not found - is this the deploy/ directory?"

UNITS=(
  fuelflow-wal-ship.service
  fuelflow-wal-ship.timer
  fuelflow-wal-ship-alert@.service
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
echo "Enabling and starting fuelflow-wal-ship.timer ..."
systemctl enable --now fuelflow-wal-ship.timer

echo
echo "Done. Next scheduled run:"
systemctl list-timers fuelflow-wal-ship.timer --no-pager || true

echo
echo "To run one shipment right now and watch it:"
echo "    systemctl start fuelflow-wal-ship.service && journalctl -fu fuelflow-wal-ship.service"
echo
echo "Note: the nightly backup still ships its own full-archive blob. That is deliberate -"
echo "it is the self-contained second copy that needs no sequence of per-segment files to"
echo "be complete. This timer is what makes the newest off-box segment about a minute old."
