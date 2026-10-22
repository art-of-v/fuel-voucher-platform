#!/usr/bin/env bash
# Tests the `changes` detector in .github/workflows/ci.yml — the shell block under the
# "Filter changed paths" step.
#
# WHY THIS NEEDS A TEST
# That block is the cost gate for a private repo AND the coverage gate: it decides which suites run
# on a PR. Get an arm wrong in the "skip" direction and a change merges with a suite that never
# ran; get it wrong in the "run" direction and a one-line PR bills twenty minutes of Testcontainers.
# Both failures are invisible from a green build — there is nothing in the log to notice.
#
# It is CI configuration, so a copy of its logic pasted into a test proves nothing: the copy would
# keep passing after ci.yml changed. So this EXTRACTS the real block out of ci.yml and runs it.
# The only edit is substituting the `${{ ... }}` GitHub expressions with literals, because there is
# no GitHub around to expand them.
#
# Run: bash scripts/changes-detector.test.sh
set -uo pipefail
cd "$(dirname "$0")/.."

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

CI_YML=".github/workflows/ci.yml"

# Pull the step's `run: |` body out of the workflow. Body lines are indented 10 spaces and the
# block ends at the next line that is not, which is the next top-level job key.
awk '
  /^      - name: Filter changed paths$/ { in_step = 1 }
  in_step && /^        run: \|$/         { in_run = 1; next }
  in_run {
    if ($0 ~ /^[[:space:]]*$/) { print ""; next }
    if ($0 !~ /^          /) exit
    sub(/^ {10}/, "")
    print
  }
' "$CI_YML" > "$WORK/detector.raw"

if ! grep -q 'case "\$f" in' "$WORK/detector.raw"; then
  echo "::error::could not extract the detector from $CI_YML - did the step get renamed or re-indented?"
  exit 1
fi

# GitHub expands these before the shell ever sees them; substitute the two shapes the step uses.
sed -e 's|\${{ github\.event_name }}|pull_request|g' \
    -e 's|\${{ github\.event\.pull_request\.base\.sha }}|$BASE|g' \
    -e 's|\${{ github\.event\.before }}|$BASE|g' \
    -e 's|\${{ github\.sha }}|$HEAD|g' \
    "$WORK/detector.raw" > "$WORK/detector.sh"

pass=0
fail=0

# Builds a throwaway repo whose ONLY base-to-head change is the listed paths, then runs the real
# detector over that diff and echoes the seven flags it wrote to $GITHUB_OUTPUT.
#
# The base commit holds one unrelated file, so every path a case adds is genuinely new and the
# diff is exactly what the case describes. Nothing but $GITHUB_OUTPUT is allowed to reach stdout.
detect() {
  local repo="$WORK/repo"
  rm -rf "$repo"; mkdir -p "$repo"
  (
    cd "$repo" || exit 1
    git init -q
    git config user.email t@t
    git config user.name t
    git config commit.gpgsign false
    git config core.autocrlf false
    git config core.safecrlf false
    echo base > README.md
    git add README.md
    git commit -qm base
  ) || { echo "setup failed"; return 1; }

  local base head f
  base="$(cd "$repo" && git rev-parse HEAD)"

  while IFS= read -r f; do
    [ -z "$f" ] && continue
    mkdir -p "$repo/$(dirname "$f")" || return 1
    echo "$f" > "$repo/$f"
    ( cd "$repo" && git add -A && git commit -qm "add $f" ) || return 1
  done <<< "$1"

  head="$(cd "$repo" && git rev-parse HEAD)"
  : > "$WORK/out"
  ( cd "$repo" && BASE="$base" HEAD="$head" GITHUB_OUTPUT="$WORK/out" bash "$WORK/detector.sh" ) \
    > "$WORK/log" 2>&1 || { echo "detector exited non-zero"; cat "$WORK/log"; return 1; }

  local flag v out=""
  for flag in backend admin mobile website deploy migrations scripts compose; do
    v="$(grep -o "^$flag=.*" "$WORK/out" | head -1)"
    [ -n "$v" ] || { echo "detector wrote no '$flag' flag"; return 1; }
    [ -n "$out" ] && out="$out "
    out="$out${v#*=}"
  done
  echo "$out"
}

ok()  { pass=$((pass + 1)); printf '  ok    %s\n' "$1"; }
bad() { fail=$((fail + 1)); printf '  FAIL  %s\n        got:      %s\n        expected: %s\n' "$1" "$2" "$3"; }

check() {
  local label="$1" diff="$2" expect="$3"
  local got
  got="$(detect "$diff")"
  if [ "$got" = "$expect" ]; then ok "$label"; else bad "$label" "$got" "$expect"; fi
}

echo "changes detector (real block extracted from $CI_YML)"

# backend admin mobile website deploy migrations scripts compose
#
# NOTE `deploy` is false in the fail-open fallback: that block sets the component flags plus
# migrations and scripts, but not deploy. So a repo-root or deploy/ change runs every COMPONENT
# suite yet still skips the deploy-scripts job - only a deploy/ path arms that one.
#
# `compose` is the LOCAL stack (the root docker-compose.yml). It is false for everything that
# cannot change what that file does - a repo-root file, a deploy/ file, a source change - and
# true only for the compose file, its .env, the two Dockerfiles it builds and the script that
# brings it up. The unclassified-file arm deliberately does NOT arm it: an unreadable diff arms
# everything (the fail-open path), but a file nobody classified is not a compose change.
ALL="true true true true false true true false"

# scripts/ gets its own job and no component suite: nothing outside scripts/ imports those
# fetchers, so a change there cannot break backend, admin, mobile or website.
check "scripts-only runs the scripts job and nothing else" \
  "$(printf 'scripts/fetch-upg-stations.mjs\nscripts/fetch-upg-stations.test.mjs\nscripts/lib/station-csv.mjs')" \
  "false false false false false false true false"

# The fail-open default: anything unclassified is treated as shared and runs the full matrix.
check "a repo-root file still runs everything" \
  "AGENTS.md" \
  "$ALL"

# deploy/ sets its own flag AND `other`, so it gets every component suite plus the deploy job.
check "deploy/ runs every component suite and the deploy job" \
  "deploy/disk-check.sh" \
  "true true true true true true true false"

# The Migrations/ arm deliberately shadows the `backend/*` arm that follows it, so a migration-only
# PR does NOT run the backend unit/integration suites. That is not a hole: dryrun-migrate runs
# `dotnet ef database update`, which compiles the project (catching a migration that does not
# build) and then applies it against production's own rows. This case pins that arrangement so a
# future edit to the case arms cannot quietly drop either half.
check "a migration runs the dry run, not the backend suites" \
  "backend/src/FuelFlow.API/Migrations/20260101_Seed.cs" \
  "false false false false false true false false"

check "backend source runs only backend" \
  "backend/src/FuelFlow.API/Program.cs" \
  "true false false false false false false false"

check "admin + scripts together run both" \
  "$(printf 'admin/src/lib/i18n.ts\nscripts/fetch-upg-stations.mjs')" \
  "false true false false false false true false"

# The LOCAL stack. Nothing else in the repository reads the root compose file, so a change to it
# runs the local-stack job ALONE - the whole matrix for a compose fix would cost twenty minutes
# to learn nothing. The job builds the API image and boots the API, so a compile error is caught
# there too.
check "the root compose file runs the local stack and nothing else" \
  "docker-compose.yml" \
  "false false false false false false false true"

check "the .env the compose file reads runs the local stack and nothing else" \
  ".env.example" \
  "false false false false false false false true"

# A Dockerfile the local stack builds arms the local-stack job AND its component suite: the
# component suite keeps today's coverage, and the job is what sees the image actually build.
check "the backend Dockerfile runs the local stack and the backend suite" \
  "backend/src/FuelFlow.API/Dockerfile" \
  "true false false false false false false true"

check "the admin Dockerfile runs the local stack and the admin suite" \
  "admin/Dockerfile" \
  "false true false false false false false true"

check "the local-stack script runs the local stack and the scripts job" \
  "scripts/check-local-stack.sh" \
  "false false false false false false true true"

echo
if [ "$fail" -eq 0 ]; then
  echo "all $pass detector case(s) pass"
else
  echo "$fail of $((pass + fail)) detector case(s) FAILED"
fi
exit "$fail"