#!/usr/bin/env bash
# Local admin-panel dev container. See docker-compose.dev-admin.yml for why it exists.
#
#   ./dev-admin.sh up          install deps into the volume, then idle
#   ./dev-admin.sh test        npm run test
#   ./dev-admin.sh typecheck
#   ./dev-admin.sh build
#   ./dev-admin.sh shell
#   ./dev-admin.sh install     force `npm ci` after a lockfile change
#   ./dev-admin.sh down
#
# The volume name carries the lockfile hash, so changing a dependency gives a fresh
# volume rather than a stale one. Checking that in the container on purpose: a
# stale node_modules produces failures nobody can reproduce, and finding it here
# is the whole point of the hash.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ADMIN_DIR="$(cd "$SCRIPT_DIR/../admin" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.dev-admin.yml"
LOCKFILE="$ADMIN_DIR/package-lock.json"

if [[ ! -f "$LOCKFILE" ]]; then
  echo "No $LOCKFILE. This script is run from the repo's deploy/ directory." >&2
  exit 1
fi

# Short hash: enough to distinguish dependency sets, short enough to type.
# Falls back when no sha tool is present rather than refusing to run.
if command -v sha256sum >/dev/null 2>&1; then
  LOCK_HASH="$(sha256sum "$LOCKFILE" | cut -c1-12)"
elif command -v shasum >/dev/null 2>&1; then
  LOCK_HASH="$(shasum -a 256 "$LOCKFILE" | cut -c1-12)"
else
  echo "No sha256sum or shasum found; every worktree will share one volume." >&2
  LOCK_HASH="nohash"
fi
export LOCK_HASH

compose() {
  # -f from an absolute path so the file resolves the same way from any cwd.
  docker compose -f "$COMPOSE_FILE" "$@"
}

case "${1:-up}" in
  up)
    compose up -d
    echo "Dependencies live in volume fuelflow-admin-modules-$LOCK_HASH."
    echo "Next: ./dev-admin.sh test"
    ;;
  install)
    # `npm ci`, not `npm install`: it installs exactly what the lockfile says and
    # deletes anything else. `npm install` would quietly write a new lockfile into
    # the worktree, which is a change nobody asked for.
    compose exec -T admin npm ci
    ;;
  test|typecheck|build|lint|shell)
    if [[ "$1" == "shell" ]]; then
      compose exec admin sh
    else
      compose exec -T admin npm run "$1"
    fi
    ;;
  down)
    compose down
    ;;
  clean)
    # Destroys the volume too. Only for when node_modules is genuinely wrong.
    compose down --volumes
    echo "Volume fuelflow-admin-modules-$LOCK_HASH removed."
    ;;
  *)
    echo "Usage: $0 {up|install|test|typecheck|build|lint|shell|down|clean}" >&2
    exit 64
    ;;
esac