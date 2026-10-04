#!/usr/bin/env bash
#
# Decide which image tag each component deploys.
#
# Every successful `image-*` job tags its image with the commit SHA, so that SHA is
# what we deploy. A component whose build FAILED gets the `main` tag instead - which
# is by construction the last image that built successfully for that component.
#
# That is the whole point: a broken build in one component must neither roll back the
# others nor hold the release. The cost is that such a component silently stays on its
# previous version, so the decision is logged loudly here and surfaced as a workflow
# warning rather than left to be discovered on the box.
#
# Input  (env): SHA, and BACKEND_RESULT / ADMIN_RESULT / WEBSITE_RESULT
#               each *_RESULT is a GitHub job `result`: success|failure|cancelled|skipped
# Output ($GITHUB_OUTPUT): backend_tag, admin_tag, website_tag, held
#
# Exits 0 when there is something to deploy. Exits 1 when NO component built - a total
# image failure is a failed release, and must stay red rather than degrade into a
# silent redeploy of the previous version.

set -euo pipefail

: "${SHA:?SHA is required}"
: "${BACKEND_RESULT:?BACKEND_RESULT is required}"
: "${ADMIN_RESULT:?ADMIN_RESULT is required}"
: "${WEBSITE_RESULT:?WEBSITE_RESULT is required}"

out="${GITHUB_OUTPUT:-/dev/null}"

tag_for() {
  if [ "$1" = "success" ]; then printf '%s' "$SHA"; else printf 'main'; fi
}

backend_tag="$(tag_for "$BACKEND_RESULT")"
admin_tag="$(tag_for "$ADMIN_RESULT")"
website_tag="$(tag_for "$WEBSITE_RESULT")"

held=()
[ "$BACKEND_RESULT" = "success" ] || held+=("backend")
[ "$ADMIN_RESULT" = "success" ] || held+=("admin")
[ "$WEBSITE_RESULT" = "success" ] || held+=("website")

{
  echo "backend_tag=$backend_tag"
  echo "admin_tag=$admin_tag"
  echo "website_tag=$website_tag"
  echo "held=${held[*]-}"
} >> "$out"

{
  echo "Release $SHA"
  echo "  backend -> $backend_tag"
  echo "  admin   -> $admin_tag"
  echo "  website -> $website_tag"
}

# Nothing built at all: there is no partial release to salvage, and quietly
# redeploying the previous version would report success for a release that never
# shipped. Fail loudly instead - this also keeps prod gated behind staging.
if [ "${#held[@]}" -eq 3 ]; then
  echo "::error::No component produced an image for $SHA (backend=$BACKEND_RESULT, admin=$ADMIN_RESULT, website=$WEBSITE_RESULT). Nothing to deploy; failing the release rather than redeploying the previous version."
  exit 1
fi

if [ "${#held[@]}" -gt 0 ]; then
  echo "::warning title=Partial release::Holding ${held[*]} on its last successful build ($backend_tag/$admin_tag/$website_tag). The other components ship normally; a fix for ${held[*]} has to reach main and deploy in a later run."
fi