#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Backup-failure notifier. Started by fuelflow-backup-alert@.service via the
# OnFailure= hook on fuelflow-backup.service — never run it by hand except to test.
#
# It reuses the SAME Telegram bot the observability stack already uses
# (TELEGRAM_BOT_TOKEN / TELEGRAM_CHAT_ID in deploy/.env), so a failed backup lands in
# the same group as "app is down" alerts. If those vars are not set, it degrades to a
# loud journal line rather than failing — the point is to never be the reason an alert
# is lost.
#
# Always exits 0: this is a notifier, not a gate. Its own failure must not mask the
# backup failure that triggered it (which is already recorded against the backup unit).
# ------------------------------------------------------------------------------
set -uo pipefail

FAILED_UNIT="${1:-fuelflow-backup.service}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST="$(hostname 2>/dev/null || echo unknown-host)"

# The last few log lines from the failed run give whoever reads the alert an immediate
# cause (missing age key, postgres down, disk full) without SSHing in first.
CONTEXT="$(journalctl -u "$FAILED_UNIT" -n 12 --no-pager -o cat 2>/dev/null || true)"

MESSAGE="🔴 FuelFlow backup FAILED on ${HOST}
unit: ${FAILED_UNIT}
time: $(date -Is)

last log lines:
${CONTEXT:-<no journal output>}

The nightly encrypted DB backup did not complete. Check:
  journalctl -u ${FAILED_UNIT} -n 50 --no-pager
Until this is fixed there is no fresh off-site copy of the database."

# Always record it in the journal — this is the fallback when Telegram is not configured
# or the API call fails.
echo "$MESSAGE" | logger -t fuelflow-backup-alert

# Load Telegram creds from the same .env the stack uses. Parse, don't source: this is the
# failure notifier, so it above all must not itself die on a spaced/quoted secret.
if [[ -f "$SCRIPT_DIR/.env" ]]; then
  # shellcheck source=load-env.sh
  source "$SCRIPT_DIR/load-env.sh"
  load_env "$SCRIPT_DIR/.env" || true
fi

if [[ -n "${TELEGRAM_BOT_TOKEN:-}" && -n "${TELEGRAM_CHAT_ID:-}" ]]; then
  # --data-urlencode so newlines and special characters in the log context survive.
  # Cap the length: Telegram rejects messages over 4096 chars.
  TEXT="${MESSAGE:0:3900}"
  if curl -fsS --max-time 20 \
      "https://api.telegram.org/bot${TELEGRAM_BOT_TOKEN}/sendMessage" \
      --data-urlencode "chat_id=${TELEGRAM_CHAT_ID}" \
      --data-urlencode "text=${TEXT}" \
      -o /dev/null 2>/dev/null; then
    echo "backup-failure alert delivered to Telegram" | logger -t fuelflow-backup-alert
  else
    echo "WARNING: Telegram delivery failed; alert is in the journal only" | logger -t fuelflow-backup-alert
  fi
else
  echo "NOTE: TELEGRAM_BOT_TOKEN/CHAT_ID not set; alert is in the journal only" | logger -t fuelflow-backup-alert
fi

exit 0
