#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Reads the attestation verdict metric and answers ONE question:
#
#     what would flipping ATTESTATION_MODE to Enforce deny right now?
#
#     ./read-attestation-verdicts.sh --prometheus http://127.0.0.1:9090
#     ./read-attestation-verdicts.sh --prometheus https://prom.example \
#         --user operator --password "$PROM_PASSWORD" --window 15m
#
# Exit 0 = measured, and Enforce would deny nothing: eligible traffic is flowing and
#          every verdict in it is valid.
# Exit 1 = measured, and Enforce WOULD deny eligible live traffic. The rate is printed.
# Exit 2 = could NOT answer, and it says which way (see below).
#
# WHY THIS EXISTS
#
# Planning #690 has one box left that no repository action can tick: "read the verdict
# metric before setting the mode". The metric exists (app PR #1706,
# `fuelflow_attestation_verdict_requests_total`, labelled `verdict`, `code`,
# `enforce_eligible`), the PromQL is in docs/APP_ATTESTATION_AND_PINNING_RUNBOOK.md, and
# nobody has run it against the box. Reading it by hand has a specific trap, and that
# trap is the whole reason this is a script:
#
#   An EMPTY result is not a green one. The runbook says it in one line - "a flat zero
#   means nothing was counted rather than that enforcing is safe" - because the series
#   exists only for the protected routes, only from the deploy that carries the code, and
#   only once a sign-in or a purchase has actually been served. A hand-run query that
#   returns no rows looks exactly like a query that returned zero refusals, and one of
#   those is a reason to flip the switch while the other is a reason not to.
#
# So the exit codes separate the answers, and they also separate a third case that is
# easy to miss: eligible traffic of exactly zero. The installed fleet sends no rollout
# headers, so it counts as NOT eligible - a nonzero total with zero eligible requests
# means the metric is watching a population the flip does not touch, which answers
# nothing.
#
# The three queries are the runbook's, unchanged, and the script prints them, so its
# output is evidence that can be pasted into the issue rather than a claim about what was
# run.
#
# The protected set is AttestationOptions.cs:126 - /api/auth/send-code, /api/auth/verify,
# the purchase routes, mark-used and restore. /api/brands, /api/app-version and
# /api/auth/user/me are NOT protected, so ordinary browsing produces no series at all.
# ------------------------------------------------------------------------------
set -uo pipefail

PROMETHEUS="${PROMETHEUS_URL:-}"
WINDOW="${ATTESTATION_WINDOW:-5m}"
USER_NAME="${PROMETHEUS_USER:-}"
PASSWORD="${PROMETHEUS_PASSWORD:-}"
METRIC="fuelflow_attestation_verdict_requests_total"
TIMEOUT="${PROMETHEUS_TIMEOUT:-10}"

usage() {
  cat <<'EOF'
Reads the attestation verdict metric: what would Enforce deny right now?

    read-attestation-verdicts.sh --prometheus URL [options]

  --prometheus URL     Prometheus base URL (or PROMETHEUS_URL)
  --window DURATION    rate() window, default 5m (or ATTESTATION_WINDOW)
  --user NAME          basic-auth user (or PROMETHEUS_USER)
  --password SECRET    basic-auth password (or PROMETHEUS_PASSWORD)
  --timeout SECONDS    per-request timeout, default 10 (or PROMETHEUS_TIMEOUT)

Exit 0 = measured, Enforce would deny nothing.
Exit 1 = measured, Enforce WOULD deny eligible traffic (rate printed).
Exit 2 = could not answer: unreachable, HTTP or parse failure, empty series, no eligible
         traffic, or a non-numeric rate. Exit 2 is never a green light.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --prometheus) PROMETHEUS="${2:-}"; shift 2 ;;
    --window) WINDOW="${2:-}"; shift 2 ;;
    --user) USER_NAME="${2:-}"; shift 2 ;;
    --password) PASSWORD="${2:-}"; shift 2 ;;
    --timeout) TIMEOUT="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 2 ;;
    *) echo "read-attestation-verdicts: unknown argument '$1'" >&2; usage >&2; exit 2 ;;
  esac
done

if [[ -z "$PROMETHEUS" ]]; then
  echo "read-attestation-verdicts: no Prometheus target." >&2
  echo "  pass --prometheus URL (or set PROMETHEUS_URL); on the box that is usually" >&2
  echo "  http://127.0.0.1:9090 while 9090 stays bound to loopback." >&2
  exit 2
fi
PROMETHEUS="${PROMETHEUS%/}"

ERR_FILE="$(mktemp)"
trap 'rm -f "$ERR_FILE"' EXIT

# One scalar query. Prints the value; on failure prints a reason on stderr and returns 2.
prom_scalar() {
  local query="$1" body value series
  local -a auth=()
  [[ -n "$USER_NAME" ]] && auth=(-u "${USER_NAME}:${PASSWORD}")

  if ! body="$(curl -sS --max-time "$TIMEOUT" "${auth[@]}" \
        --get --data-urlencode "query=${query}" "${PROMETHEUS}/api/v1/query" 2>&1)"; then
    echo "prometheus unreachable: ${body}" >&2
    return 2
  fi
  if [[ "$body" != *'"status":"success"'* ]]; then
    echo "prometheus rejected the query: ${body:0:300}" >&2
    return 2
  fi
  # A vector result carries one "value":[<ts>,"<n>"] per series, and a sum() is one series.
  # An empty vector carries none - the case this script exists to keep apart from a zero.
  series="$(grep -o '"value":\[' <<< "$body" | wc -l | tr -d ' ')"
  if [[ "$series" == "0" ]]; then
    echo "the series is empty" >&2
    return 2
  fi
  value="$(sed -n 's/.*"value":\[[0-9.eE+-]*,"\([^"]*\)"\].*/\1/p' <<< "$body" | head -1)"
  if [[ -z "$value" ]]; then
    echo "could not parse a value out of: ${body:0:300}" >&2
    return 2
  fi
  printf '%s' "$value"
}

Q_TOTAL="sum(rate(${METRIC}[${WINDOW}]))"
Q_ELIGIBLE="sum(rate(${METRIC}{enforce_eligible=\"true\"}[${WINDOW}]))"
Q_REFUSED="sum(rate(${METRIC}{enforce_eligible=\"true\",verdict!=\"valid\"}[${WINDOW}]))"

echo "attestation verdicts - what Enforce would deny"
echo "  prometheus : $PROMETHEUS"
echo "  window     : $WINDOW"
echo "  queries    :"
echo "    $Q_TOTAL"
echo "    $Q_ELIGIBLE"
echo "    $Q_REFUSED"
echo

fail() {
  local why="$1"
  echo "RESULT: could not answer - $why" >&2
  echo "  This is NOT evidence that Enforce is safe. docs/APP_ATTESTATION_AND_PINNING_RUNBOOK.md" >&2
  echo "  explains why an absent series is expected: the protected set is small, the installed" >&2
  echo "  fleet sends no proof, and a counter has no series until it is served." >&2
  exit 2
}

read_rate() {
  local name="$1" query="$2" value
  if ! value="$(prom_scalar "$query" 2>"$ERR_FILE")"; then
    fail "the ${name} rate could not be read: $(tr -d '\n' < "$ERR_FILE")"
  fi
  if ! awk -v n="$value" 'BEGIN { exit (n ~ /^-?[0-9]*\.?[0-9]+([eE][-+]?[0-9]+)?$/) ? 0 : 1 }'; then
    fail "the ${name} rate is '${value}', which is not a number (too few samples for rate()?)"
  fi
  printf '%s' "$value"
}

total="$(read_rate "total" "$Q_TOTAL")"
eligible="$(read_rate "eligible" "$Q_ELIGIBLE")"
refused="$(read_rate "refused" "$Q_REFUSED")"

printf '  all protected requests   : %s/s\n' "$total"
printf '  eligible (rollout-aware) : %s/s\n' "$eligible"
printf '  refused if enforcing     : %s/s\n' "$refused"
echo

if ! awk -v e="$eligible" 'BEGIN { exit (e > 0) ? 0 : 1 }'; then
  fail "no eligible traffic was seen, so nothing can say what Enforce would deny"
fi

if awk -v r="$refused" 'BEGIN { exit (r > 0) ? 0 : 1 }'; then
  echo "RESULT: flipping now would deny ${refused}/s of eligible live traffic."
  echo "  Those requests carry a rollout header and fail verification, so Enforce would"
  echo "  refuse them. Find out which app version and which verdict before touching the mode."
  exit 1
fi

echo "RESULT: measured, and Enforce would deny nothing at this rate."
echo "  ${eligible}/s of eligible traffic is flowing and every verdict in it is valid."
echo "  Re-read the runbook's two limits before flipping: enforce_eligible comes from"
echo "  forgeable headers (a rollout measure, not the control), and a signed-in user is"
echo "  only counted once they actually sign in or buy."
exit 0
