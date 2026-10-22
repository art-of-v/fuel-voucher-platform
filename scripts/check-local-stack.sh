#!/usr/bin/env bash
# Brings the LOCAL stack up for real and asserts that it works.
#
# WHY THIS NEEDS TO EXIST
# The root `docker-compose.yml` is the README's documented quick start, and it was broken in five
# ways at once: a service that built an image that does not exist, an API pointed at a database
# that was never created, a healthcheck asking for a third database name, an API started without
# the key it refuses to start without, and an admin nginx with no backend to proxy to.
#
# Every other suite in this repository is green with that file broken. The backend tests do not
# read it, the deploy scripts test the PRODUCTION compose file, and the admin suite does not know
# the container exists. The file a new developer's first command reads was the one file nothing
# checked - so this script checks it, in CI, on every change to it.
#
# WHAT IT PROVES
#   1. the file parses and every variable it interpolates is present - `docker compose config`
#      fails on a missing `:?`-guarded value, so an incomplete `.env.example` is an error here
#      and not a crash-loop on somebody's machine;
#   2. the images build - a service that points at a Dockerfile that does not exist dies here,
#      exactly as it died for the `mobile-app` service;
#   3. the stack comes up and the API answers `/health/ready`, which is the readiness check that
#      covers BOTH the database and Redis - and the API refuses to boot without the QR cipher key,
#      so the boot refusals are covered too;
#   4. the admin SPA is served, which is the check that catches an nginx that cannot proxy.
#
# The `.env` it uses is built from `.env.example` - the file a developer is told to copy - so the
# example is proved to be enough, and a variable added to one side and not the other fails here.
#
# Run: bash scripts/check-local-stack.sh
#
# The stack runs under its OWN project name, so it never touches a developer's own `docker
# compose up` (different containers, different volume) - but it publishes the same ports, so
# stop the other one first if it is running.
set -euo pipefail

cd "$(dirname "$0")/.."

PROJECT="fuelflow-local-check"
API_URL="http://127.0.0.1:5000/health/ready"
ADMIN_URL="http://127.0.0.1:5002/"
READY_TIMEOUT_SECONDS="${READY_TIMEOUT_SECONDS:-300}"

log() { printf '%s\n' "$*"; }
fail() { printf '::error::%s\n' "$*" >&2; exit 1; }

command -v docker >/dev/null 2>&1 || fail "docker is not installed - the local stack needs it."
docker compose version >/dev/null 2>&1 || fail "docker compose v2 is not available."
command -v curl >/dev/null 2>&1 || fail "curl is not installed - the readiness check needs it."

WORK="$(mktemp -d)"
ENV_FILE="$WORK/.env"

cleanup() {
  local status=$?
  if [ "${KEEP_STACK:-0}" = "1" ]; then
    log "KEEP_STACK=1 - leaving the stack up (project '$PROJECT')."
  else
    log "Tearing the stack down (project '$PROJECT')."
    docker compose --env-file "$ENV_FILE" -p "$PROJECT" down -v --remove-orphans >/dev/null 2>&1 || true
  fi
  rm -rf "$WORK"
  exit $status
}
trap cleanup EXIT

# --- The .env, built from .env.example -------------------------------------------------
# Everything the example ships is used as-is; only the QR cipher key is filled in, because it
# has to decode to exactly 32 bytes and no example value can be one. A value the example leaves
# empty stays empty - that is the point: if the compose file needs it, the `:?` guard fires and
# this script fails, which is exactly what happened to the API's database credentials.
if [ ! -f .env.example ]; then
  fail ".env.example is missing - the local stack's variables are documented there."
fi

qr_key=""
if command -v openssl >/dev/null 2>&1; then
  qr_key="$(openssl rand -base64 32)"
else
  qr_key="$(head -c 32 /dev/urandom | base64 | tr -d '\n')"
fi
[ -n "$qr_key" ] || fail "could not generate a QR cipher key."

sed "s|^QrCipher__Key=.*$|QrCipher__Key=$qr_key|" .env.example > "$ENV_FILE"
grep -q "^QrCipher__Key=." "$ENV_FILE" \
  || fail ".env.example has no QrCipher__Key line - the compose file passes it to the API, which refuses to start without it."

# The ports are published on 127.0.0.1, so a running stack of the developer's own would take
# them. Say so plainly rather than letting compose fail with "port is already allocated".
for port in 5000 5002 5433 6379; do
  if (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; then
    fail "port $port is already in use - stop the other stack first (docker compose down)."
  fi
done

# --- 1. The file resolves ---------------------------------------------------------------
log "== docker compose config =="
docker compose --env-file "$ENV_FILE" -p "$PROJECT" config >/dev/null \
  || fail "docker compose config failed - the compose file does not resolve with .env.example."

# --- 2. The images build and the stack comes up ------------------------------------------
log "== docker compose up --build =="
docker compose --env-file "$ENV_FILE" -p "$PROJECT" up -d --build \
  || fail "docker compose up --build failed - see the output above."

# --- 3. The API answers ------------------------------------------------------------------
log "== waiting for $API_URL (up to ${READY_TIMEOUT_SECONDS}s) =="
deadline=$(( $(date +%s) + READY_TIMEOUT_SECONDS ))
ready=""
while [ "$(date +%s)" -lt "$deadline" ]; do
  body="$(curl -fsS --max-time 5 "$API_URL" 2>/dev/null || true)"
  case "$body" in
    *Healthy*)
      ready="$body"
      break
      ;;
  esac
  sleep 3
done

if [ -z "$ready" ]; then
  log "---- the API did not become ready; here is what it said ----"
  docker compose --env-file "$ENV_FILE" -p "$PROJECT" logs --tail 80 dotnet-backend || true
  fail "the API never answered /health/ready - the stack does not come up."
fi
log "the API is ready: $ready"

# --- 4. The admin is served -------------------------------------------------------------
log "== waiting for $ADMIN_URL =="
deadline=$(( $(date +%s) + READY_TIMEOUT_SECONDS ))
admin_ok=""
while [ "$(date +%s)" -lt "$deadline" ]; do
  code="$(curl -fsS -o /dev/null -w '%{http_code}' --max-time 5 "$ADMIN_URL" 2>/dev/null || true)"
  if [ "$code" = "200" ]; then
    admin_ok="$code"
    break
  fi
  sleep 3
done

if [ -z "$admin_ok" ]; then
  log "---- the admin did not answer; here is what it said ----"
  docker compose --env-file "$ENV_FILE" -p "$PROJECT" logs --tail 40 admin-frontend || true
  fail "the admin SPA never answered on $ADMIN_URL - the stack does not come up."
fi
log "the admin answers $admin_ok"

log "the local stack comes up: the API is healthy and the admin is served."
