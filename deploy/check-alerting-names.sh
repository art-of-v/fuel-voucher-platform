#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Resolves every name the alerting chain uses against the compose services that
# actually exist.
#
#     bash deploy/check-alerting-names.sh
#
# WHY THIS EXISTS
#
# On 2026-10-20 the review of the production database found that the entire alerting
# chain was blind, and that the fix was one string:
#
#     deploy/observability/prometheus/prometheus.yml
#         - job_name: fuelflow-api
#           static_configs:
#             - targets: ["dotnet-backend:8080"]
#
# `dotnet-backend` was not a service anybody ran. The target did not resolve, so
# Prometheus scraped NOTHING from the API, and every rule that reads a metric the API
# emits - the server error rate, the request latency, the voucher pool, the failed
# fulfilment, the failed Hangfire job, the payment stuck in pending, the refresh-token
# reuse spike - was an `absent()` that could never fire.
#
# No Alertmanager is configured, so the Grafana-managed rules are the only channel that
# actually notifies anybody (the second, native copy of them was removed for exactly that
# reason). They read the same metrics, so the Telegram channel was blind for the same reason.
#
# The fix landed, and nothing checks it. This is the SECOND time a rename has silently
# broken a name in this file, and the first time it did, the whole chain went dark for a
# day. A check that resolves every name against the compose services turns that class of
# breakage into a failed build instead of a silent outage.
#
# WHAT IT CHECKS
#
#   1. Every scrape target in the Prometheus config must name a service in
#      deploy/docker-compose.prod.yml - the stack Prometheus runs beside.
#   2. Every datasource URL in the Grafana provisioning must name a service in
#      deploy/docker-compose.observability.yml - the stack it is provisioned from.
#
# A target that names nothing outside itself (localhost, an IP) is Prometheus scraping
# itself, and that is not a service and not a mistake.
#
# WHY IT DOES NOT USE `docker compose config --services`
#
# That list is the canonical one, and it is also the one that needs a complete env file -
# every required variable has to be set before Compose will render the file. A gate that
# cannot run in CI is a gate that only runs on the box, on the day somebody renames a
# service, which is the day it is too late to be useful. The service keys are read out of
# the compose file itself, which is the same list and needs nothing.
#
# EXIT
#   0  every name resolves
#   1  a name does not resolve, and the file says which one and where
#   2  a file this check reads is missing
# ------------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

PROMETHEUS_FILE="deploy/observability/prometheus/prometheus.yml"
DATASOURCES_FILE="backend/observability/grafana/provisioning/datasources/datasources.yml"
PROD_COMPOSE="deploy/docker-compose.prod.yml"
OBSERVABILITY_COMPOSE="deploy/docker-compose.observability.yml"

failures=0

fail() { echo "ERROR: $*" >&2; failures=$((failures + 1)); }
die()  { echo "ERROR: $*" >&2; exit 2; }

for f in "$PROMETHEUS_FILE" "$DATASOURCES_FILE" "$PROD_COMPOSE" "$OBSERVABILITY_COMPOSE"; do
  [[ -r "$f" ]] || die "$f is missing or unreadable; the alerting chain cannot be checked against a file that is not there."
done

# The service names of a compose file: the keys under `services:`, and nothing else.
#
# The awk stops at the next top-level key, because `networks:` and `volumes:` are
# top-level too and their keys are not services - `default` and `shared` are networks
# here, and a check that thought they were services would pass a target that resolves to
# nothing.
compose_services() {
  awk '
    /^services:[[:space:]]*$/ { in_services = 1; next }
    /^[^[:space:]#]/ { if (in_services) exit }
    in_services && /^  [A-Za-z0-9_.-]+:[[:space:]]*$/ {
      name = $1
      sub(/:$/, "", name)
      print name
    }
  ' "$1" | sort -u
}

# A host that names nothing outside itself: Prometheus scraping itself, or a fixed
# address. Neither is a service, and neither is a mistake.
is_external() {
  case "$1" in
    localhost|127.0.0.1|0.0.0.0|::1|host.docker.internal) return 0 ;;
    # An IPv4 literal.
    [0-9]*.[0-9]*.[0-9]*.[0-9]*) return 0 ;;
    # An IPv6 literal.
    *:*) [[ "$1" =~ ^[0-9a-fA-F:]+$ ]] && return 0 ;;
  esac
  return 1
}

# The host part of "host:port" or of "scheme://host:port".
host_of() {
  local target="$1"
  target="${target#*://}"
  target="${target%%/*}"
  # A bracketed IPv6 literal keeps its brackets and its port inside them.
  if [[ "$target" == \[*\]* ]]; then
    echo "${target%%\]*}" | tr -d '[]'
    return 0
  fi
  # A bare IPv6 literal has colons that are not a port separator.
  if [[ "$target" == *:*:* ]]; then
    echo "$target"
    return 0
  fi
  echo "${target%%:*}"
}

echo "== the alerting chain's names =="

prod_services="$(compose_services "$PROD_COMPOSE")"
observability_services="$(compose_services "$OBSERVABILITY_COMPOSE")"

# ---- the scrape targets
echo "-- prometheus scrape targets"
targets="$(grep -oE 'targets:[[:space:]]*\[[^]]*\]' "$PROMETHEUS_FILE" \
  | grep -oE '"[^"]+"' \
  | tr -d '"' \
  | sort -u || true)"

if [[ -z "$targets" ]]; then
  fail "$PROMETHEUS_FILE declares no scrape targets; Prometheus would scrape nothing, which is the failure this check exists to prevent"
else
  for target in $targets; do
    host="$(host_of "$target")"
    if is_external "$host"; then
      echo "  $target -> outside the compose (not a service)"
      continue
    fi
    if grep -qxF "$host" <<<"$prod_services"; then
      echo "  $target -> service '$host'"
    else
      fail "the Prometheus target '$target' in $PROMETHEUS_FILE names '$host', and no such service exists in $PROD_COMPOSE. Prometheus would resolve it to nothing and every rule that reads an API metric would be an absent() that can never fire. The service names are: $(tr '\n' ' ' <<<"$prod_services")"
    fi
  done
fi

# ---- the datasources the alerting rules read through
echo "-- grafana datasource urls"
urls="$(grep -oE 'url:[[:space:]]*[^[:space:]]+' "$DATASOURCES_FILE" \
  | sed -E 's/^url:[[:space:]]*//' \
  | sort -u || true)"

if [[ -z "$urls" ]]; then
  fail "$DATASOURCES_FILE declares no datasource urls; the Grafana-managed rules would have nothing to read"
else
  for url in $urls; do
    host="$(host_of "$url")"
    if is_external "$host"; then
      echo "  $url -> outside the compose (not a service)"
      continue
    fi
    if grep -qxF "$host" <<<"$observability_services"; then
      echo "  $url -> service '$host'"
    else
      fail "the Grafana datasource '$url' in $DATASOURCES_FILE names '$host', and no such service exists in $OBSERVABILITY_COMPOSE. The alerting rules read their metrics through this datasource, so a name that resolves to nothing is an alerting chain that cannot fire. The service names are: $(tr '\n' ' ' <<<"$observability_services")"
    fi
  done
fi

if [[ $failures -gt 0 ]]; then
  echo "ERROR: $failures name(s) in the alerting chain do not resolve." >&2
  exit 1
fi

echo "every name in the alerting chain resolves"
