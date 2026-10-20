#!/usr/bin/env bash
# Tests deploy/check-script-modes.sh - the check that keeps a documented command from
# failing with "Permission denied".
#
# The bug it guards against is real and was live: three scripts that the runbooks and
# their own headers tell the operator to RUN - ./rebuild-collation.sh,
# ./drill-restore.sh, ./dev-admin.sh - carried mode 100644 in git, and the CI deploy
# step does not chmod anything, so that is the mode that landed on the box.
#
# The test works on a throwaway git repository, so it can make a file non-executable
# and see whether the check notices.
# ------------------------------------------------------------------------------
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CHECK="$SCRIPT_DIR/check-script-modes.sh"

pass=0
fail=0
check() {
  local what="$1" expected="$2" actual="$3"
  if [[ "$expected" == "$actual" ]]; then
    echo "  PASS  $what"; pass=$((pass + 1))
  else
    echo "  FAIL  $what (expected '$expected', got '$actual')"; fail=$((fail + 1))
  fi
}

TMP="$(mktemp -d "${TMPDIR:-/tmp}/script-modes-test.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT

# A throwaway repository with the same shape as the real one: a runnable script, a
# sourced one, and a file with no shebang.
REPO="$TMP/repo"
mkdir -p "$REPO/deploy"
cd "$REPO" || exit 1
git init -q .
git config user.email "test@example.com"
git config user.name "Test"

printf '#!/usr/bin/env bash\necho hi\n' > deploy/runnable.sh
printf '#!/usr/bin/env bash\nfoo() { :; }\n' > deploy/load-env.sh
printf 'no shebang here\n' > deploy/plain.txt
printf '#!/usr/bin/env bash\ntrue\n' > deploy/also-runnable.sh
git add -A > /dev/null
git update-index --chmod=+x deploy/runnable.sh deploy/also-runnable.sh
git commit -qm "initial"

# The check reads the INDEX of the repository it is in, and takes the list of scripts
# from it, so it has to run against the throwaway repo. It is copied in and pointed at
# that repo's deploy directory.
cp "$CHECK" "$REPO/deploy/check-script-modes.sh"
chmod +x "$REPO/deploy/check-script-modes.sh"
git -C "$REPO" add deploy/check-script-modes.sh > /dev/null
git -C "$REPO" update-index --chmod=+x deploy/check-script-modes.sh

echo "=== a repository where every mode is right ==="
out="$(cd "$REPO" && bash deploy/check-script-modes.sh 2>&1)"; rc=$?
check "it exits 0" "0" "$rc"

echo "=== a runnable script that is not executable is caught ==="
git -C "$REPO" update-index --chmod=-x deploy/runnable.sh
out="$(cd "$REPO" && bash deploy/check-script-modes.sh 2>&1)"; rc=$?
check "it exits 1" "1" "$rc"
if [[ "$out" == *"runnable.sh"* ]]; then
  echo "  PASS  it names the file"; pass=$((pass + 1))
else
  echo "  FAIL  it did not name the file"; fail=$((fail + 1))
fi
if [[ "$out" == *"Permission denied"* ]]; then
  echo "  PASS  it says what the failure looks like"; pass=$((pass + 1))
else
  echo "  FAIL  it did not say what the failure looks like"; fail=$((fail + 1))
fi

echo "=== and the fix is what it says ==="
git -C "$REPO" update-index --chmod=+x deploy/runnable.sh
out="$(cd "$REPO" && bash deploy/check-script-modes.sh 2>&1)"; rc=$?
check "it exits 0 once the mode is fixed" "0" "$rc"

echo "=== a SOURCED script that is executable is caught ==="
# load-env.sh and pitr-common.sh are sourced by the other scripts; nothing runs them.
# Making one executable is a sign that somebody has confused it for a program.
git -C "$REPO" update-index --chmod=+x deploy/load-env.sh
out="$(cd "$REPO" && bash deploy/check-script-modes.sh 2>&1)"; rc=$?
check "it exits 1" "1" "$rc"
if [[ "$out" == *"load-env.sh"* ]]; then
  echo "  PASS  it names the file"; pass=$((pass + 1))
else
  echo "  FAIL  it did not name the file"; fail=$((fail + 1))
fi

echo "=== the REAL repository ==="
out="$(cd "$SCRIPT_DIR/.." && bash deploy/check-script-modes.sh 2>&1)"; rc=$?
if [[ "$rc" -eq 0 ]]; then
  echo "  PASS  every script in deploy/ has the right mode"; pass=$((pass + 1))
else
  echo "  FAIL  the real repository has a script with the wrong mode:"
  echo "$out" | sed 's/^/        /'
  fail=$((fail + 1))
fi

echo
echo "passed: $pass  failed: $fail"
[[ "$fail" -eq 0 ]] || exit 1
exit 0
