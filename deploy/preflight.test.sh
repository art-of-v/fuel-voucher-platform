#!/usr/bin/env bash
# Proves the env-file scan and the caddy-credential check catch what actually broke
# production, and do not reject configurations that work.

set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFLIGHT="$SCRIPT_DIR/preflight.sh"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

pass=0; fail=0
check() { # description expected_exit actual_exit
  local desc="$1" want="$2" got="$3"
  if [[ "$got" == "$want" ]]; then echo "  PASS  $desc"; pass=$((pass+1));
  else echo "  FAIL  $desc (exit $got, wanted $want)"; fail=$((fail+1)); fi
}

# Git does not always carry the executable bit across a checkout, and a mode that
# silently degrades to 644 turns every assertion below into "Permission denied" -
# which is exactly what happened, and it held the production deploy for two runs
# before anyone looked at why the step was red. Assert it instead.
if [[ -x "$PREFLIGHT" ]]; then
  echo "  PASS  preflight.sh is executable"
  pass=$((pass+1))
else
  echo "  FAIL  preflight.sh is NOT executable (git mode 644). 'git update-index --chmod=+x deploy/preflight.sh'"
  fail=$((fail+1))
  chmod +x "$PREFLIGHT" 2>/dev/null || true
fi

# ------------------------------------------------------------------------------
# The real .env.staging on the box had a line 1 of `\# ...` instead of `# ...`.
# Compose rejects it outright, and because the workflow chains staging before
# production, that skipped the production deploy too - main red for two merges.
# ------------------------------------------------------------------------------

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

printf '\\# FuelFlow STAGING env (gitignored, box-only).\nROOT_DOMAIN=staging.palne.shop\n' > "$TMP/broken-comment.env"
out=$("$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/broken-comment.env" 2>&1); rc=$?
check "a backslash before '#' is rejected" 1 "$rc"
[[ "$out" == *"broken-comment.env:1"* ]] \
  && { echo "  PASS  names the offending line number"; pass=$((pass+1)); } \
  || { echo "  FAIL  does not name the line: $out"; fail=$((fail+1)); }
[[ "$out" != *"FuelFlow STAGING"* ]] \
  && { echo "  PASS  does not echo the file's contents"; pass=$((pass+1)); } \
  || { echo "  FAIL  leaked env contents into the output"; fail=$((fail+1)); }

printf 'ROOT_DOMAIN=x\nthis is not an assignment\n' > "$TMP/garbage.env"
"$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/garbage.env" >/dev/null 2>&1
check "a non-assignment line is rejected" 1 "$?"

printf '# comment\r\nROOT_DOMAIN=x\r\n' > "$TMP/crlf.env"
"$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/crlf.env" >/dev/null 2>&1
check "CRLF line endings are tolerated" 0 "$?"

# ------------------------------------------------------------------------------
# The staging basic-auth hash, checked on the value caddy will actually receive.
#
# Measured on the box: .env held 63 chars with SIX `$`, caddy received 60 with
# THREE - compose collapses `$$` to `$` and passes a lone `$` through unchanged.
#
# So a partially escaped hash still collapses into a well-formed one and still
# WORKS. The first version of this check rejected it, on the belief that partial
# escaping silently breaks the credential. It does not, and rejecting it would
# have blocked a working deploy - which is how a gate loses its credibility.
# ------------------------------------------------------------------------------
TAIL='abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQ'
if [[ ${#TAIL} -ne 53 ]]; then
  echo "  FAIL  fixture tail is ${#TAIL} chars, bcrypt needs 53"
  exit 1
fi
HASH="\$2a\$14\$$TAIL"
DOUBLED="\$\$2a\$\$14\$\$$TAIL"
PARTIAL="\$\$2a\$14\$$TAIL"

printf 'ROOT_DOMAIN=x\nSTAGING_BASICAUTH_HASH=%s\n' "$DOUBLED" > "$TMP/hash-ok.env"
"$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/hash-ok.env" >/dev/null 2>&1
check "a fully escaped bcrypt hash is accepted" 0 "$?"

printf 'ROOT_DOMAIN=x\nSTAGING_BASICAUTH_HASH=%s\n' "$HASH" > "$TMP/hash-bare.env"
"$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/hash-bare.env" >/dev/null 2>&1
check "an unescaped bcrypt hash is accepted (compose passes a lone \$ through)" 0 "$?"

printf 'ROOT_DOMAIN=x\nSTAGING_BASICAUTH_HASH=%s\n' "$PARTIAL" > "$TMP/hash-partial.env"
"$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/hash-partial.env" >/dev/null 2>&1
check "a partially escaped bcrypt hash is accepted - rejecting it would block a working deploy" 0 "$?"

# The state the box was actually in: a random string, not a hash. This is the one
# that stops caddy, and with it prod's only public ingress.
printf 'ROOT_DOMAIN=x\nSTAGING_BASICAUTH_HASH=aRandomStringNotEvenClose\n' > "$TMP/hash-garbage.env"
out=$("$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/hash-garbage.env" 2>&1); rc=$?
check "a non-hash value is rejected" 1 "$rc"
[[ "$out" == *"REFUSE TO START"* ]] \
  && { echo "  PASS  says what it does to caddy, not merely that the shape is wrong"; pass=$((pass+1)); } \
  || { echo "  FAIL  message does not name the consequence: $out"; fail=$((fail+1)); }

printf 'ROOT_DOMAIN=x\nSTAGING_BASICAUTH_HASH=$$2a$$14$$tooshort\n' > "$TMP/hash-short.env"
"$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/hash-short.env" >/dev/null 2>&1
check "a truncated hash is rejected" 1 "$?"

# Unset is legitimate: the Caddyfile documents staging basic auth as inert until it
# is set, so this warns and carries on rather than failing the deploy.
printf 'ROOT_DOMAIN=x\n' > "$TMP/hash-absent.env"
out=$("$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/hash-absent.env" 2>&1); rc=$?
check "an unset hash warns but does not fail" 0 "$rc"
[[ "$out" == *"not set"* ]] \
  && { echo "  PASS  warns that staging basic auth is inert"; pass=$((pass+1)); } \
  || { echo "  FAIL  did not warn: $out"; fail=$((fail+1)); }

# Quoted values are how an app password is usually written; compose strips one
# layer, so the check must too or it rejects a working file.
printf "ROOT_DOMAIN=x\nSTAGING_BASICAUTH_HASH='%s'\n" "$DOUBLED" > "$TMP/hash-quoted.env"
"$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/hash-quoted.env" >/dev/null 2>&1
check "a quoted bcrypt hash is accepted" 0 "$?"

# ------------------------------------------------------------------------------
# The hash is caddy's credential, read from deploy/.env - not from .env.staging.
#
# caddy is the production service; STAGING_BASICAUTH_* means "the staging vhosts
# prod's caddy fronts", not "a variable the staging backend owns". docker-compose.
# prod.yml lists it under caddy's `environment:` and pulls it from .env; the
# staging compose file never mentions it.
#
# The check ran once per env file anyway, so every single preflight run on the box
# printed ".env.staging: STAGING_BASICAUTH_HASH not set; staging basic auth stays
# inert" while staging was gated and working. Assert the silence, not the warning:
# a preflight that cries wolf on a healthy deploy is one people learn to skip.
# ------------------------------------------------------------------------------
FIXTURE="$TMP/preflight-fixture"
mkdir -p "$FIXTURE"
cp "$PREFLIGHT" "$FIXTURE/preflight.sh"
cat > "$FIXTURE/.env" <<EOF
ROOT_DOMAIN=palne.shop
STAGING_BASICAUTH_HASH=$DOUBLED
EOF
cat > "$FIXTURE/.env.staging" <<'EOF'
# FuelFlow STAGING env (gitignored, box-only).
ROOT_DOMAIN=staging-app.palne.shop
POSTGRES_PASSWORD=whatever
JWT_SECRET=abc123
EOF
out=$(cd "$FIXTURE" && bash ./preflight.sh --env-only 2>&1); rc=$?
check "a defaults run with a valid prod .env passes" 0 "$rc"
# .env.staging SHOULD still be scanned - for structure. It is caddy's credential
# it has no business holding, so assert only that the false warning is gone.
[[ "$out" != *".env.staging: STAGING_BASICAUTH_HASH"* ]] \
  && { echo "  PASS  .env.staging is not asked for caddy's credential"; pass=$((pass+1)); } \
  || { echo "  FAIL  preflight still expects STAGING_BASICAUTH_HASH in .env.staging: $out"; fail=$((fail+1)); }
[[ "$out" == *".env.staging: ok"* ]] \
  && { echo "  PASS  .env.staging is still scanned for structure"; pass=$((pass+1)); } \
  || { echo "  FAIL  the fix went too far and stopped checking .env.staging at all: $out"; fail=$((fail+1)); }
[[ "$out" == *".env: STAGING_BASICAUTH_HASH reaches caddy"* ]] \
  && { echo "  PASS  the credential check runs against .env"; pass=$((pass+1)); } \
  || { echo "  FAIL  the credential check does not name .env: $out"; fail=$((fail+1)); }

# The converse must still hold, or the fix would have silenced a real warning.
cat > "$FIXTURE/.env" <<'EOF'
ROOT_DOMAIN=palne.shop
EOF
out=$(cd "$FIXTURE" && bash ./preflight.sh --env-only 2>&1); rc=$?
check "a prod .env missing the hash still warns" 0 "$rc"
[[ "$out" == *".env"* && "$out" == *"not set"* ]] \
  && { echo "  PASS  the warning names .env, the file caddy actually reads"; pass=$((pass+1)); } \
  || { echo "  FAIL  lost the warning for the file that matters: $out"; fail=$((fail+1)); }

# ------------------------------------------------------------------------------
# The wording of the failure must not assert a cause that measurement disproved.
#
# "A partially escaped hash validates and boots and then answers 401 to
# everything" was in preflight for two deploys. It is false - a partially escaped
# hash collapses into a valid one and works (measured). A gate that explains its
# failure wrongly costs more debugging time than one that stays quiet.
# ------------------------------------------------------------------------------
printf 'ROOT_DOMAIN=x\nSTAGING_BASICAUTH_HASH=aRandomStringNotEvenClose\n' > "$TMP/hash-garbage2.env"
out=$("$PREFLIGHT" --no-defaults --env-only --env-file "$TMP/hash-garbage2.env" 2>&1)
[[ "$out" != *"partially escaped"* ]] \
  && { echo "  PASS  the failure message does not blame partial escaping"; pass=$((pass+1)); } \
  || { echo "  FAIL  still blames escaping, which measurement disproved: $out"; fail=$((fail+1)); }
[[ "$out" == *"base64-encoded plaintext"* ]] \
  && { echo "  PASS  names what caddy will actually try to do with the value"; pass=$((pass+1)); } \
  || { echo "  FAIL  does not name the real consequence: $out"; fail=$((fail+1)); }

echo
echo "  $pass passed, $fail failed"
[[ $fail -eq 0 ]]