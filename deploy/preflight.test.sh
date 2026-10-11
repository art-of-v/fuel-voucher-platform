#!/usr/bin/env bash
# Proves the env-file scan catches what actually broke the staging deploy.
#
# The real .env.staging on the box had a line 1 of `\# ...` instead of `# ...`
# and an unquoted value containing a space and a `>`. Compose rejects the first
# outright and `source` chokes on the second; this asserts the scan reports both
# and still accepts a clean file.

set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFLIGHT="$SCRIPT_DIR/preflight.sh"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

pass=0; fail=0
check() { # description expected_exit
  local desc="$1" want="$2" got="$3"
  if [[ "$got" == "$want" ]]; then echo "  PASS  $desc"; pass=$((pass+1));
  else echo "  FAIL  $desc (exit $got, wanted $want)"; fail=$((fail+1)); fi
}

# A clean file.
cat > "$TMP/good.env" <<'EOF'
# FuelFlow STAGING env (gitignored, box-only).
ROOT_DOMAIN=staging-app.palne.shop

POSTGRES_PASSWORD=with spaces and 'quotes'
JWT_SECRET=abc123
EOF
out=$("$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/good.env" 2>&1); rc=$?
check "a clean env file is accepted" 0 "$rc"
[[ "$out" == *"good.env: ok (3 variables)"* ]] \
  && { echo "  PASS  reports the variable count"; pass=$((pass+1)); } \
  || { echo "  FAIL  reports the variable count: $out"; fail=$((fail+1)); }

# The real defect: a backslash before the hash is not a comment to Compose.
printf '\\# FuelFlow STAGING env (gitignored, box-only).\nROOT_DOMAIN=staging.palne.shop\n' > "$TMP/broken-comment.env"
out=$("$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/broken-comment.env" 2>&1); rc=$?
check "a backslash before '#' is rejected" 1 "$rc"
[[ "$out" == *"broken-comment.env:1"* ]] \
  && { echo "  PASS  names the offending line number"; pass=$((pass+1)); } \
  || { echo "  FAIL  does not name the line: $out"; fail=$((fail+1)); }
[[ "$out" != *"FuelFlow STAGING"* ]] \
  && { echo "  PASS  does not echo the file's contents"; pass=$((pass+1)); } \
  || { echo "  FAIL  leaked env contents into the output"; fail=$((fail+1)); }

# A stray line that is neither blank, comment, nor assignment.
printf 'ROOT_DOMAIN=x\nthis is not an assignment\n' > "$TMP/garbage.env"
"$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/garbage.env" >/dev/null 2>&1
check "a non-assignment line is rejected" 1 "$?"

# CRLF endings must not be mistaken for breakage - editors on Windows add them.
printf '# comment\r\nROOT_DOMAIN=x\r\n' > "$TMP/crlf.env"
"$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/crlf.env" >/dev/null 2>&1
check "CRLF line endings are tolerated" 0 "$?"

echo
echo "  $pass passed, $fail failed"
[[ $fail -eq 0 ]]