#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Checks that the Grafana alerting provisioning LOADS.
#
#     bash deploy/check-alerting-provisioning.sh
#
# WHY THIS EXISTS
#
# The Telegram contact point is the first thing Grafana's alerting provisioning
# reads, and if it does not load, the provisioning of EVERYTHING after it dies
# with it - the rules included - and Grafana exits 1 and restart-loops. The
# reason it did not load on the production box is worth writing down, because it
# is a YAML type, not a typo:
#
#   Grafana substitutes $VAR into the provisioning file and parses the result
#   again. A Telegram chat id is a negative number, so it arrives as a YAML
#   NUMBER, and Grafana's Telegram integration wants a string:
#
#     cannot unmarshal number into Go struct field Config.chatid of type string
#
#   The error is in the Grafana log, in the middle of a startup that otherwise
#   looks fine, and the box keeps restart-looping. The metric-based alerting
#   was never running there: the stack had been started without the Telegram
#   overlay, and with it Grafana would not start at all - so the nine hours the
#   API was down on 2026-10-21/22 produced no alert, and the only alert anyone
#   got was the nightly backup one, which comes from systemd and not from here.
#
# Quoting the placeholder does not help either. The quotes are consumed with
# the substitution, so the value arrives with its quotes inside it, the
# provisioning loads, and every notification fails with
# "failed to send telegram message: webhook response status 400 Bad Request".
# The chat id therefore ships as a literal; see the provisioning file.
#
# WHAT IT DOES
#
# Runs the Grafana image the compose file pins, with the alerting provisioning
# and the datasources mounted where Grafana expects them, and reads the startup
# log. The image is read out of the compose file, so the check cannot drift
# from what the box runs.
#
# It looks for three things, because they fail in three different ways:
#
#   1. "Failed to provision alerting"  - the contact point does not load, and
#                                       the rules go with it.
#   2. "finished to provision alerting" - the provisioning never ran (a missing
#                                       file, a wrong path).
#   3. "Failed to build rule evaluator" - a rule's model is broken, so the rule
#                                       never evaluates and the chain is
#                                       switched off while it looks switched on.
#                                       This is the one that needs the container
#                                       to live long enough to evaluate once.
#
# EXIT
#   0  the provisioning loads
#   1  it does not, and the log says why
#   2  docker is not available, or a file this check reads is missing
# ------------------------------------------------------------------------------
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

PROVISIONING_DIR="backend/observability/grafana/provisioning/alerting-telegram"
CONTACT_POINT="$PROVISIONING_DIR/contact-points.yml"
RULES_FILE="$PROVISIONING_DIR/alert-rules.yml"
DATASOURCES_DIR="backend/observability/grafana/provisioning/datasources"
COMPOSE_FILE="deploy/docker-compose.observability.yml"

fail() { echo "ERROR: $*" >&2; exit 1; }
die()  { echo "ERROR: $*" >&2; exit 2; }

[[ -f "$CONTACT_POINT" ]] || die "$CONTACT_POINT is missing; there is nothing to load."
[[ -f "$RULES_FILE" ]] || die "$RULES_FILE is missing; there are no rules to load."
[[ -f "$COMPOSE_FILE" ]] || die "$COMPOSE_FILE is missing; the Grafana image cannot be resolved."

command -v docker >/dev/null 2>&1 || die "docker is not available; this check loads the provisioning in the Grafana image."

# The image the stack runs, read out of the compose file - the check must not be
# able to pass against a version nobody runs.
GRAFANA_IMAGE="$(grep -E '^[[:space:]]+image:[[:space:]]+grafana/grafana:' "$COMPOSE_FILE" \
  | head -1 | sed -E 's/.*image:[[:space:]]*//' | tr -d '"'"'"'')"
[[ -n "$GRAFANA_IMAGE" ]] || die "$COMPOSE_FILE does not name a grafana/grafana image."

TMP="$(mktemp -d "${TMPDIR:-/tmp}/alerting-provisioning.XXXXXX")"
cleanup() { rm -rf "$TMP"; }
trap cleanup EXIT

# The whole alerting provisioning: the contact point, the rules, and the datasources
# the rules name. The datasources are mounted because the rules reference them by
# uid, and a rule whose datasource does not exist is a rule that never evaluates -
# so provisioning them is the point of the check. Their URLs point at hosts that do
# not exist in here, which does not matter: the check is about the configuration
# loading, not about the data.
mkdir -p "$TMP/provisioning/alerting" "$TMP/provisioning/datasources"
cp "$CONTACT_POINT" "$TMP/provisioning/alerting/contact-points.yml"
cp "$RULES_FILE" "$TMP/provisioning/alerting/alert-rules.yml"
if [[ -d "$DATASOURCES_DIR" ]]; then
  cp "$DATASOURCES_DIR"/*.yml "$TMP/provisioning/datasources/" 2>/dev/null || true
fi

echo "== the alerting provisioning loads =="
echo "  image: $GRAFANA_IMAGE"

# A sample token: the check is about the TYPES in the file, not about the token.
# Nothing is sent anywhere - the rules have no data to alert on in here.
LOG="$TMP/grafana.log"
timeout 60 docker run --rm --name alerting-provisioning-check \
  -e GF_SECURITY_ADMIN_PASSWORD=check \
  -e TELEGRAM_BOT_TOKEN=123456:ABC-check \
  -v "$TMP/provisioning:/etc/grafana/provisioning:ro" \
  "$GRAFANA_IMAGE" > "$LOG" 2>&1 || true

if grep -q "Failed to provision alerting" "$LOG"; then
  echo "ERROR: the alerting provisioning does not load:" >&2
  grep -A2 "Failed to provision alerting" "$LOG" | head -6 >&2
  exit 1
fi

if ! grep -q "finished to provision alerting" "$LOG"; then
  echo "ERROR: the alerting provisioning never finished (the log says nothing about it):" >&2
  tail -20 "$LOG" >&2
  exit 1
fi

if grep -q "Failed to build rule evaluator" "$LOG"; then
  echo "ERROR: a rule does not evaluate:" >&2
  grep "Failed to build rule evaluator" "$LOG" | head -3 | sed -E 's/.*(rule_uid=[^ ]+).*(error="[^"]*").*/  \1 \2/' >&2
  exit 1
fi

echo "  the contact point and the rules load"
