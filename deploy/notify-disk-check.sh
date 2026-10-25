#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Disk-pressure notifier. Started by fuelflow-disk-check-alert@.service via the
# OnFailure= hook on fuelflow-disk-check.service - never run by hand except to test.
# Mirrors notify-prune-failure.sh.
#
# fuelflow-disk-check.service exits non-zero whenever it has something to say: a warning, a
# critical, a recovery, or the check's own inability to measure the disk. The severity and the
# detail are already in the journal - that is where disk-check.sh writes them - so this unit's only
# job is to get them to a human.
#
# Always exits 0: a notifier must not mask the failure it reports (that is already recorded
# against fuelflow-disk-check.service).
# ------------------------------------------------------------------------------
set -uo pipefail

FAILED_UNIT="${1:-fuelflow-disk-check.service}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST="$(hostname 2>/dev/null || echo unknown-host)"

# disk-check.sh prints the whole report - severity, percentages, database size and the commands
# to run - as its log lines, so the journal excerpt IS the message. -n 40 rather than the 20 the
# prune notifier uses: the disk messages carry a command block an operator may want to copy.
CONTEXT="$(journalctl -u "$FAILED_UNIT" -n 40 --no-pager -o cat 2>/dev/null || true)"

MESSAGE="💾 FuelFlow disk pressure on ${HOST}
unit: ${FAILED_UNIT}
time: $(date -Is)

${CONTEXT:-<no journal output - fuelflow-disk-check.service failed without logging>}

Background: on 2026-09-27 / reached 100% and Postgres crash-looped, taking the API down. The
nightly GC reclaims images and build cache only; database growth, container logs and Loki data are
not its to reclaim. See docs/DEPLOYMENT.md (private docs repo), "Disk hygiene".

Full detail:
    journalctl -u ${FAILED_UNIT} -n 50 --no-pager"

# Always record it in the journal - the fallback when Telegram is not configured or fails.
echo "$MESSAGE" | logger -t fuelflow-disk-check-alert

# Load Telegram creds from the same .env.production the stack uses. Parse, don't source (see load-env.sh):
# a notifier must not itself die on a spaced/quoted secret.
if [[ -f "$SCRIPT_DIR/.env.production" ]]; then
  # shellcheck source=load-env.sh
  source "$SCRIPT_DIR/load-env.sh"
  load_env "$SCRIPT_DIR/.env.production" || true
fi

if [[ -n "${TELEGRAM_BOT_TOKEN:-}" && -n "${TELEGRAM_CHAT_ID:-}" ]]; then
  # Cap the length: Telegram rejects messages over 4096 chars. --data-urlencode so newlines and
  # special characters in the log context survive.
  TEXT="${MESSAGE:0:3900}"
  if curl -fsS --max-time 20 \
      "https://api.telegram.org/bot${TELEGRAM_BOT_TOKEN}/sendMessage" \
      --data-urlencode "chat_id=${TELEGRAM_CHAT_ID}" \
      --data-urlencode "text=${TEXT}" \
      -o /dev/null 2>/dev/null; then
    echo "disk-pressure alert delivered to Telegram" | logger -t fuelflow-disk-check-alert
  else
    echo "WARNING: Telegram delivery failed; alert is in the journal only" | logger -t fuelflow-disk-check-alert
  fi
else
  echo "NOTE: TELEGRAM_BOT_TOKEN/CHAT_ID not set; alert is in the journal only" | logger -t fuelflow-disk-check-alert
fi

exit 0