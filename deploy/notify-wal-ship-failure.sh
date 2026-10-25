#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# WAL off-box shipping failure notifier. Started by fuelflow-wal-ship-alert@.service via
# the OnFailure= hook on fuelflow-wal-ship.service - never run by hand except to test.
# Mirrors notify-backup-failure.sh and notify-prune-failure.sh.
#
# The shipper exits non-zero when it cannot encrypt or upload a segment, or when it
# cannot write the status object. In every case the data is still on the box, so this is
# an "the copy that survives the host is getting older" alarm, not data loss yet:
#
#   * the archive on the box is intact;
#   * the newest segment is simply not off-box;
#   * the host-loss RPO grows with every missed run.
#
# Always exits 0: a notifier must not mask the failure it reports (that is already
# recorded against the unit that failed).
# ------------------------------------------------------------------------------
set -uo pipefail

FAILED_UNIT="${1:-fuelflow-wal-ship.service}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST="$(hostname 2>/dev/null || echo unknown-host)"

CONTEXT="$(journalctl -u "$FAILED_UNIT" -n 25 --no-pager -o cat 2>/dev/null || true)"

SUBJECT="FuelFlow WAL OFF-BOX SHIPPING FAILED"
ADVICE="A WAL segment could not be encrypted or uploaded, or the status object could not be
written. The archive on the box is intact, but the copy that survives losing the host is
now older than it should be. Find out why before the next one:
    journalctl -u ${FAILED_UNIT} -n 50 --no-pager
    rclone lsf \${BACKUP_REMOTE}/wal | tail
    rclone cat \${BACKUP_REMOTE}/wal/status.txt
    systemctl list-timers fuelflow-wal-ship.timer
Common causes: the rclone remote or its credentials, the age recipient, DNS/network, or
the archive directory not being readable."

MESSAGE="🔴 ${SUBJECT} on ${HOST}
unit: ${FAILED_UNIT}
time: $(date -Is)

last log lines:
${CONTEXT:-<no journal output>}

${ADVICE}"

# Always record it in the journal - the fallback when Telegram is not configured or fails.
echo "$MESSAGE" | logger -t fuelflow-wal-ship-alert

# Load Telegram creds from the same .env.production the stack uses. Parse, don't source (see
# load-env.sh): the failure notifier above all must not itself die on a spaced/quoted
# secret.
if [[ -f "$SCRIPT_DIR/.env.production" ]]; then
  # shellcheck source=load-env.sh
  source "$SCRIPT_DIR/load-env.sh"
  load_env "$SCRIPT_DIR/.env.production" || true
fi

if [[ -n "${TELEGRAM_BOT_TOKEN:-}" && -n "${TELEGRAM_CHAT_ID:-}" ]]; then
  # Cap the length: Telegram rejects messages over 4096 chars. --data-urlencode so
  # newlines and special characters in the log context survive.
  TEXT="${MESSAGE:0:3900}"
  if curl -fsS --max-time 20 \
      "https://api.telegram.org/bot${TELEGRAM_BOT_TOKEN}/sendMessage" \
      --data-urlencode "chat_id=${TELEGRAM_CHAT_ID}" \
      --data-urlencode "text=${TEXT}" \
      -o /dev/null 2>/dev/null; then
    echo "wal-ship-failure alert delivered to Telegram" | logger -t fuelflow-wal-ship-alert
  else
    echo "WARNING: Telegram delivery failed; alert is in the journal only" | logger -t fuelflow-wal-ship-alert
  fi
else
  echo "NOTE: TELEGRAM_BOT_TOKEN/CHAT_ID not set; alert is in the journal only" | logger -t fuelflow-wal-ship-alert
fi

exit 0
