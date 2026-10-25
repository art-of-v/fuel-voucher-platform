#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Restart the production stack the way the deploy workflow restarts it.
#
#     cd ~/FuelFlow/deploy && ./restart.sh                 # restart what is already running
#     cd ~/FuelFlow/deploy && ./restart.sh --tag <sha>    # move to a specific build first
#     cd ~/FuelFlow/deploy && ./restart.sh --services fuelflow-backend caddy
#
# WHY THIS EXISTS
#
# On 2026-10-11 the production API was taken down twice in one session, both times
# by hand and both times by the same shape of mistake:
#
#   * `docker compose up -d` with no *_IMAGE_TAG exported falls back to the mutable
#     `:main` tag, which on this box held an older image than the one running. The
#     backend crash-looped on a startup guard that current code no longer contains,
#     so the deployed fix was not actually the fix.
#   * `up -d` without a `caddy validate` first. The Caddyfile had a
#     STAGING_BASICAUTH_HASH that was never a bcrypt hash; caddy refused to start
#     and took prod's only public ingress with it.
#
# Both are checkable before anything is touched, which is what preflight.sh does.
# This script runs it, pins the tags, restarts, and waits for health - so the
# order that is safe is also the order that is easy.
#
# It deliberately does NOT pull by default. A restart should not change versions.
# Pass --tag to move deliberately, and then the pull happens with that exact tag.
# ------------------------------------------------------------------------------

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# The compose service names and the container names do not correspond. Guessing
# this cost a check that silently passed on the real box - see the note in
# preflight.sh, where the same mistake made it report three containers as
# "(not running)" while raising nothing.
service_container() {
  case "$1" in
    fuelflow-backend)  echo "fuelflow-backend" ;;
    admin-frontend)  echo "fuelflow-admin" ;;
    website-frontend) echo "fuelflow-website" ;;
    *) echo "fuelflow-$1" ;;
  esac
}

TAG=""
SERVICES=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tag) TAG="${2:?--tag needs a value}"; shift 2 ;;
    --services) shift; while [[ $# -gt 0 && "$1" != --* ]]; do SERVICES+=("$1"); shift; done ;;
    -h|--help) sed -n '2,30p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

echo "== preflight =="
./preflight.sh

# ------------------------------------------------------------------------------
# Pin the image tags.
#
# Read them off the containers that are running RIGHT NOW, so the compose files
# resolve to exactly the images that are already proven in production. `--tag`
# overrides, which is the only supported way to change versions from here.
# ------------------------------------------------------------------------------
if [[ -z "$TAG" ]]; then
  echo
  echo "== pinning the currently running images =="
  for svc in fuelflow-backend admin-frontend website-frontend; do
    ctr="$(service_container "$svc")"
    img=$(docker inspect "$ctr" --format '{{.Config.Image}}' 2>/dev/null || true)
    if [[ -z "$img" ]]; then
      echo "  $ctr is not running; cannot pin a tag for it" >&2
      exit 1
    fi
    t="${img##*:}"
    case "$svc" in
      fuelflow-backend) BACKEND_IMAGE_TAG="$t" ;;
      admin-frontend) ADMIN_IMAGE_TAG="$t" ;;
      website-frontend) WEBSITE_IMAGE_TAG="$t" ;;
    esac
    echo "  $ctr -> $t"
  done
  export BACKEND_IMAGE_TAG ADMIN_IMAGE_TAG WEBSITE_IMAGE_TAG
else
  echo
  echo "== using the requested tag $TAG =="
  export BACKEND_IMAGE_TAG="$TAG" ADMIN_IMAGE_TAG="$TAG" WEBSITE_IMAGE_TAG="$TAG"
  docker compose --env-file .env.production -f docker-compose.prod.yml pull
fi

echo
echo "== restarting =="
if [[ ${#SERVICES[@]} -gt 0 ]]; then
  docker compose --env-file .env.production -f docker-compose.prod.yml up -d "${SERVICES[@]}"
else
  docker compose --env-file .env.production -f docker-compose.prod.yml up -d
fi

# The Caddyfile is bind-mounted as a SINGLE file. Git updates files by atomic
# rename, so after a checkout the container's bind mount still pins the OLD inode:
# even `caddy reload` re-reads the stale contents and a hand edit never applies.
# Force-recreate so a fresh container re-binds the new file. This mirrors the
# deploy workflow, which does the same for the same reason.
docker compose --env-file .env.production -f docker-compose.prod.yml up -d --force-recreate caddy

echo
echo "== what is running =="
docker compose --env-file .env.production -f docker-compose.prod.yml images

echo
echo "== waiting for health =="
for _ in $(seq 1 24); do
  code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 https://api.palne.shop/health || echo 000)
  if [[ "$code" == "200" ]]; then
    echo "  api.palne.shop/health -> 200"
    curl -s --max-time 10 https://api.palne.shop/health; echo
    exit 0
  fi
  sleep 5
done

echo "ERROR: the API did not report healthy within 120s. Recent backend output:" >&2
docker logs --tail 40 fuelflow-backend >&2 || true
exit 1