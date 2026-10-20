#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Checks that every script in deploy/ that is meant to be RUN carries the
# executable bit, and that the ones that are meant to be SOURCED do not.
#
#     bash deploy/check-script-modes.sh
#
# WHY THIS EXISTS
#
# Three scripts in this directory are documented as being run - `./rebuild-collation.sh`,
# `./drill-restore.sh`, `./dev-admin.sh` - and the runbook for the collation rebuild is
# in DEPLOYMENT.md, in this repository's planning repo, and in the script's own header.
# All three were 100644 in git. **On a fresh checkout the documented command fails with
# "Permission denied"**, and nothing in the pipeline says so: the CI deploy step does
# not chmod anything, so the mode that lands on the box is the mode in git.
#
# That was not hypothetical. Measured on the production box on 2026-10-20:
#
#     -rw-r--r-- 1 root root 14569 Oct 20 08:00 rebuild-collation.sh
#     -rw-r--r-- 1 root root 12195 Oct 19 11:05 drill-restore.sh
#     -rw-r--r-- 1 root root  2550 Oct 18 13:07 dev-admin.sh
#
# The collation rebuild ran on 2026-10-19 and again on staging on 2026-10-20 because
# somebody had chmod-ed the file by hand on that box. A box restored from a fresh
# checkout, or a new operator's clone, would have got "Permission denied" and no
# explanation.
#
# The rule, and it is short:
#
#   - a script with a shebang that is RUN gets mode 755;
#   - a script that is SOURCED gets mode 644, and is listed in SOURCED below.
#
# The sourced ones are the exception and the reason the list exists: `load-env.sh` and
# `pitr-common.sh` define functions for other scripts to pull in, and nothing runs
# them.
#
# Exit 0 = every mode is right. Exit 1 = at least one is wrong, and it says which.
# ------------------------------------------------------------------------------
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# The scripts that are SOURCED rather than run. Each one is sourced by name somewhere
# in this directory; nothing executes it.
SOURCED=(
  load-env.sh
  pitr-common.sh
)

is_sourced() {
  local name="$1"
  for s in "${SOURCED[@]}"; do
    [[ "$name" == "$s" ]] && return 0
  done
  return 1
}

# `git ls-files -s` prints "<mode> <sha> <stage>\t<path>". The mode is the third field.
# This reads the INDEX, not the working tree, because the index is what a commit
# records and what a checkout produces.
#
# The paths are relative to the REPOSITORY ROOT, not to this directory, so the pathspec
# and the lookup agree. Getting that wrong makes the check read no files at all - and a
# check that reads no files passes, which is the failure mode this whole script exists
# to prevent.
REPO_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel 2>/dev/null)"
[[ -n "$REPO_ROOT" ]] || { echo "ERROR: $SCRIPT_DIR is not inside a git repository." >&2; exit 2; }

mode_of() {
  git -C "$REPO_ROOT" ls-files -s -- "$1" | awk '{print $1}'
}

fail=0
count=0
while IFS= read -r file; do
  name="$(basename "$file")"
  count=$((count + 1))

  # A file with no shebang is not a program; the mode does not matter.
  head -1 "$REPO_ROOT/$file" | grep -q '^#!' || continue

  mode="$(mode_of "$file")"
  if [[ -z "$mode" ]]; then
    echo "  FAIL  $name is not in the index" >&2
    fail=$((fail + 1))
    continue
  fi

  if is_sourced "$name"; then
    if [[ "$mode" != "100644" ]]; then
      echo "  FAIL  $name is SOURCED, so it must be 100644, and it is $mode" >&2
      fail=$((fail + 1))
    fi
    continue
  fi

  if [[ "$mode" != "100755" ]]; then
    echo "  FAIL  $name is run, so it must be 100755, and it is $mode" >&2
    echo "        (chmod +x it and: git update-index --chmod=+x $file)" >&2
    fail=$((fail + 1))
  fi
done < <(git -C "$REPO_ROOT" ls-files -- 'deploy/*.sh' | sort)

if [[ "$count" -eq 0 ]]; then
  echo "ERROR: no scripts were found. A check that reads no files passes, and that is" >&2
  echo "       exactly the failure mode this script exists to prevent." >&2
  exit 2
fi

if [[ "$fail" -gt 0 ]]; then
  echo >&2
  echo "ERROR: $fail script(s) have the wrong executable bit." >&2
  echo "       A script that is run and is not executable fails with 'Permission denied'" >&2
  echo "       at the first command of the runbook that invokes it." >&2
  exit 1
fi

echo "all $count deploy scripts have the right mode."
exit 0
