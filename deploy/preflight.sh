#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Check the deploy state BEFORE anything is pulled, recreated or restarted.
#
#     cd ~/FuelFlow/deploy && ./preflight.sh
#
# Every check here exists because it caught a real outage on 2026-10-11, and in
# each case the failure mode was the same: the mistake was invisible until a
# container had already been replaced. A preflight that runs first turns all four
# into a non-event.
#
#   1. Env files that Docker Compose can actually read
#   2. The rendered Caddy config
#   3. Upstream names that mean one container instead of two
#   4. Image tags, and whether a mutable `:main` could quietly win
#
# Exits non-zero on the first hard failure, so `restart.sh` can `set -e` on it.
# Warnings never fail the run - they are printed and the script continues.
# ------------------------------------------------------------------------------

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

PROD_COMPOSE=(docker compose --env-file .env -f docker-compose.prod.yml)

warnings=()
fail() { echo "ERROR: $*" >&2; exit 1; }
warn() { echo "WARNING: $*" >&2; warnings+=("$*"); }

# --env-only stops after step 1 and --env-file checks an extra path, so the env
# scan can be exercised against a known-broken file instead of trusted.
ENV_ONLY=0
NO_DEFAULTS=0
EXTRA_ENV_FILES=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --env-only) ENV_ONLY=1; shift ;;
    # --no-defaults skips .env/.env.staging so the scan can be exercised against a
    # fixture; on the box the real files are always wanted.
    --no-defaults) NO_DEFAULTS=1; shift ;;
    --env-file) EXTRA_ENV_FILES+=("${2:?--env-file needs a path}"); shift 2 ;;
    -h|--help) sed -n '2,22p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

# ------------------------------------------------------------------------------
# 1. Env files Compose can read
#
# Compose parses `--env-file` itself and rejects anything that is not blank, a
# `#` comment, or a KEY=VALUE with a shell-shaped key. The box's .env.staging had
# a line 1 of `\# FuelFlow STAGING env ...` - a backslash before the hash, which
# is NOT a comment to Compose - so `Deploy to staging` died at that line, and
# because the workflow runs staging before production, `Deploy to Hetzner` was
# skipped too. main was red for two merges with nothing in the diff to explain it.
#
# This check is deliberately a LITERAL scan and does not source anything: sourcing
# executes the file, which is exactly what load-env.sh exists to avoid.
# ------------------------------------------------------------------------------
check_env_file() {
  local file="$1" required="$2"
  [[ -f "$file" ]] || { [[ "$required" == "required" ]] && fail "$file not found" || warn "$file not found (skipping)"; return 0; }

  local bad=0 line_no=0 line
  while IFS= read -r line || [[ -n "$line" ]]; do
    line_no=$((line_no + 1))
    line="${line%$'\r'}"
    [[ "$line" =~ ^[[:space:]]*$ ]] && continue
    [[ "$line" =~ ^[[:space:]]*# ]] && continue
    [[ "$line" =~ ^[[:space:]]*[A-Za-z_][A-Za-z0-9_]*= ]] && continue
    bad=1
    echo "  $file:$line_no: not a blank line, a '#' comment, or KEY=VALUE:" >&2
    # The line itself is not printed: env files hold secrets, and a mangled line is
    # exactly the kind that is worth pasting into a ticket.
    echo "      starts with: $(printf '%s' "${line:0:12}" | cat -v)" >&2
  done < "$file"

  [[ $bad -eq 0 ]] || fail "$file has lines Compose cannot parse (see above). A leading backslash before '#' is the usual cause."
  echo "  $file: ok ($(grep -cE '^[[:space:]]*[A-Za-z_][A-Za-z0-9_]*=' "$file") variables)"
}

echo "== env files =="
if [[ "$NO_DEFAULTS" -eq 0 ]]; then
  check_env_file .env required
  check_env_file .env.staging optional
fi
for extra in ${EXTRA_ENV_FILES+"${EXTRA_ENV_FILES[@]}"}; do
  check_env_file "$extra" optional
done

if [[ "$ENV_ONLY" -eq 1 ]]; then
  echo "env-only run: stopping after step 1"
  [[ ${#warnings[@]} -gt 0 ]] && echo "Preflight passed with ${#warnings[@]} warning(s)." || echo "Preflight passed."
  exit 0
fi

# ------------------------------------------------------------------------------
# 2. The rendered Caddy config
#
# Copied verbatim from the deploy workflow on purpose: this is the check whose
# absence cost an outage. The Caddyfile is bind-mounted as a SINGLE file, so a
# hand edit that rewrites the inode (sed -i does) leaves the running container
# reading the OLD contents - and a config that only fails at container start
# means prod's only public ingress is down until someone notices.
# ------------------------------------------------------------------------------
echo "== caddy config =="
if ! docker compose --env-file .env -f docker-compose.prod.yml run --rm --no-deps \
      --entrypoint caddy caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null 2>&1; then
  echo "  (re-running with output)" >&2
  docker compose --env-file .env -f docker-compose.prod.yml run --rm --no-deps \
    --entrypoint caddy caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile 2>&1 \
    | grep -viE '^\{"level":"info"' >&2 || true
  fail "Caddyfile does not validate. Nothing has been restarted."
fi
echo "  Caddyfile: valid"

# ------------------------------------------------------------------------------
# 3. Upstream names that mean one container
#
# Production and staging run on this box and caddy is attached to both
# fuelflow_default and fuelflow_shared. Staging joins the shared network under the
# same service-name aliases, so before app#977 the name `dotnet-backend` resolved
# to the STAGING backend from caddy's point of view. Production then answered
#
#     400 Invalid Hostname
#
# on every request, because staging's AllowedHosts has no api.palne.shop - while
# the admin SPA and the marketing site were exposed to the same coin flip.
#
# Nothing in normal operation caught it: caddy holds its DNS answer for the life
# of its connections, so the correct resolution stayed cached until the first
# restart of both. The test is therefore structural - an upstream name must not be
# claimed by containers from more than one compose project - rather than "does it
# resolve to the right IP right now", which is only true by luck.
# ------------------------------------------------------------------------------
echo "== caddy upstreams =="
upstreams="$(grep -oE '^[[:space:]]*reverse_proxy[[:space:]]+(@?[A-Za-z0-9_-]+[[:space:]]+)?[A-Za-z0-9_.-]+:[0-9]+' Caddyfile \
  | grep -oE '[A-Za-z0-9_.-]+:[0-9]+' \
  | cut -d: -f1 \
  | grep -vE '^\{|\$' \
  | sort -u || true)"

if [[ -z "$upstreams" ]]; then
  echo "  no container upstreams found (nothing to check)"
else
  for name in $upstreams; do
    owners=""
    for c in $(docker ps --format '{{.Names}}'); do
      if docker inspect "$c" \
          --format "{{range \$net,\$cfg := .NetworkSettings.Networks}}{{range \$cfg.Aliases}}{{if eq . \"$name\"}}x{{end}}{{end}}{{end}}" \
          2>/dev/null | grep -q x; then
        owners="$owners $c"
      fi
    done
    count=$(printf '%s\n' $owners | grep -c . || true)
    if [[ "$count" -gt 1 ]]; then
      fail "upstream '$name' is claimed by ${count} containers:${owners//$/,}. Caddy would pick one by DNS ordering - this is exactly the prod-serves-staging bug (planning#220). Rename one side so the name is unique."
    elif [[ "$count" -eq 1 ]]; then
      echo "  $name ->${owners}"
    else
      warn "upstream '$name' matches no running container; the next start would fail with 502"
    fi
  done
fi

# ------------------------------------------------------------------------------
# 4. Image tags
#
# `docker compose up -d` without *_IMAGE_TAG falls back to the mutable `:main`
# tag, which on this box can be an older image than the one running. That is not
# hypothetical: a tagless restart after a deploy put a 3-commit-old backend on
# production, and it crash-looped on a startup guard that the current code no
# longer has. restart.sh exists so that cannot happen by hand - this check only
# reports it.
# ------------------------------------------------------------------------------
echo "== images =="
stale=0
for svc in dotnet-backend admin-frontend website-frontend; do
  running=$(docker inspect "fuelflow-$svc" --format '{{.Config.Image}}' 2>/dev/null || echo "(not running)")
  echo "  fuelflow-$svc: $running"
  if docker image inspect "ghcr.io/art-of-v/fuelflow-${svc/frontend/backend}:main" >/dev/null 2>&1; then
    warn "a mutable ':main' tag exists locally; a tagless 'up -d' could silently redeploy it"
    stale=1
  fi
done
[[ $stale -eq 1 ]] && echo "  (restart.sh pins the running tags, so this is informational here)"

echo
if [[ ${#warnings[@]} -gt 0 ]]; then
  echo "Preflight passed with ${#warnings[@]} warning(s). Review them before restarting."
else
  echo "Preflight passed."
fi