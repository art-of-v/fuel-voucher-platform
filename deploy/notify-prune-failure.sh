#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Docker-GC failure notifier. Started by fuelflow-prune-alert@.service via the
# OnFailure= hook on fuelflow-prune.service — never run by hand except to test.
# Mirrors notify-backup-failure.sh.
#
# fuelflow-prune.service exits non-zero in two cases, both worth a human's eyes:
#   1. the prune itself errored (Docker daemon unreachable, etc.), or
#   2. the prune ran but / is STILL above the alert threshold — i.e. disk is filling
#      from something prune cannot reclaim (container logs, Loki, DB growth).
# Either way this pushes to the SAME Telegram group as the app/backup alerts.
#
# Always exits 0: a notifier must not mask the failure it reports (that is already
# recorded against fuelflow-prune.service).
# ------------------------------------------------------------------------------
set -uo pipefail

FAILED_UNIT="${1:-fuelflow-prune.service}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST="$(hostname 2>/dev/null || echo unknown-host)"

# The last log lines carry the reason: the still-full + top-consumers dump, or a docker error.
CONTEXT="$(journalctl -u "$FAILED_UNIT" -n 20 --no-pager -o cat 2>/dev/null || true)"

MESSAGE="🔴 FuelFlow Docker GC FAILED on ${HOST}
unit: ${FAILED_UNIT}
time: $(date -Is)

last log lines:
${CONTEXT:-<no journal output>}

Either the nightly prune errored, or disk is still high after it (container logs / Loki /
DB growth that prune cannot reclaim). Investigate before Postgres runs out of space:
  journalctl -u ${FAILED_UNIT} -n 50 --no-pager
  df -h / && docker system df"

# Always record it in the journal — the fallback when Telegram is not configured or fails.
echo "$MESSAGE" | logger -t fuelflow-prune-alert

# Load Telegram creds from the same .env the stack uses. Parse, don't source (see load-env.sh):
# the failure notifier above all must not itself die on a spaced/quoted secret.
if [[ -f "$SCRIPT_DIR/.env" ]]; then
  # shellcheck source=load-env.sh
  source "$SCRIPT_DIR/load-env.sh"
  load_env "$SCRIPT_DIR/.env" || true
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
    echo "prune-failure alert delivered to Telegram" | logger -t fuelflow-prune-alert
  else
    echo "WARNING: Telegram delivery failed; alert is in the journal only" | logger -t fuelflow-prune-alert
  fi
else
  echo "NOTE: TELEGRAM_BOT_TOKEN/CHAT_ID not set; alert is in the journal only" | logger -t fuelflow-prune-alert
fi

exit 0
