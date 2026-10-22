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
# The compose files get parse-checked too, so they have to exist here. Copying the REAL
# ones rather than writing a stub makes every fixture run an actual assertion that the
# repo's compose files parse - which is the defect this suite exists to catch.
cp "$SCRIPT_DIR/docker-compose.prod.yml" "$SCRIPT_DIR/docker-compose.staging.yml" "$FIXTURE/"
# The env templates come along too, so the preflight's verbatim template check - the one that
# compares a deployed value against the template's own - is exercised by every run below rather
# than skipped for want of a file.
cp "$SCRIPT_DIR/.env.production.example" "$SCRIPT_DIR/.env.staging.example" "$FIXTURE/"
# Compose exits non-zero BOTH for an unparseable file and for an unset ${VAR:?}, so a
# non-zero exit only means "did not load" once every required variable is satisfied.
# Derive them from the files themselves so this cannot drift as compose grows. Matching only the
# alphanumeric run after `${` and stopping there is deliberate: a BRE like `\${NAME:\?` relies on
# `\?` meaning "optional", which silently yields the name without its `:?` guard and leaves a
# stray `:` on the end of half of them.
grep -hoE '\$\{[A-Za-z0-9_]+' "$FIXTURE"/docker-compose.*.yml \
  | sed 's/^\${//' | sort -u > "$TMP/required-vars"
# The env files have to stay COMPLETE. preflight.sh parse-checks the compose files on every
# run, and compose resolves ${VAR:?} against .env, so a deliberately minimal .env - which most of
# the assertions below want - would make the compose step fail and take the exit code with it. So
# the fixture carries every required variable, and the tests that need one absent remove it
# temporarily and restore GOOD_ENV afterwards.
{
  echo "ROOT_DOMAIN=palne.shop"
  echo "STAGING_BASICAUTH_HASH=$DOUBLED"
  while read -r var; do
    [[ -n "$var" ]] && printf '%s=dummy-for-parse-check\n' "$var"
  done < "$TMP/required-vars"
} > "$FIXTURE/.env"
{
  echo "ROOT_DOMAIN=staging-app.palne.shop"
  while read -r var; do
    [[ -n "$var" ]] && printf '%s=dummy-for-parse-check\n' "$var"
  done < "$TMP/required-vars"
} > "$FIXTURE/.env.staging"
# A variable that is both set above and listed by compose ends up here twice, and check_env_file
# reads the FIRST occurrence - so the dummy would win and STAGING_BASICAUTH_HASH would be
# rejected as "not a bcrypt hash". Keep the first line for each key, which is the explicit one.
for envfile in "$FIXTURE/.env" "$FIXTURE/.env.staging"; do
  awk -F= '!seen[$1]++' "$envfile" > "$envfile.tmp" && mv "$envfile.tmp" "$envfile"
done
GOOD_ENV="$TMP/env-complete"
GOOD_ENV_STAGING="$TMP/env-staging-complete"
cp "$FIXTURE/.env" "$GOOD_ENV"
cp "$FIXTURE/.env.staging" "$GOOD_ENV_STAGING"

# The two environments must not share the QR cipher key, so the fixture gives them different
# ones - the dummy above wrote the SAME value into both files, which is exactly the defect the
# new check looks for.
sed -i 's|^QR_CIPHER_KEY=.*|QR_CIPHER_KEY=prod-only-key|' "$FIXTURE/.env"
sed -i 's|^QR_CIPHER_KEY=.*|QR_CIPHER_KEY=staging-only-key|' "$FIXTURE/.env.staging"
cp "$FIXTURE/.env" "$GOOD_ENV"
cp "$FIXTURE/.env.staging" "$GOOD_ENV_STAGING"

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
sed -i '/^STAGING_BASICAUTH_HASH=/d' "$FIXTURE/.env"
out=$(cd "$FIXTURE" && bash ./preflight.sh --env-only 2>&1); rc=$?
cp "$GOOD_ENV" "$FIXTURE/.env"
check "a prod .env missing the hash still warns" 0 "$rc"
[[ "$out" == *".env"* && "$out" == *"not set"* ]] \
  && { echo "  PASS  the warning names .env, the file caddy actually reads"; pass=$((pass+1)); } \
  || { echo "  FAIL  lost the warning for the file that matters: $out"; fail=$((fail+1)); }

# ------------------------------------------------------------------------------
# The two environments must not share the QR cipher key.
#
# It encrypts the redeemable QR payload of every voucher at rest, and it is the only thing
# that can read it. Staging used production's key until 2026-10-21, so anyone who reached
# staging could decrypt production's vouchers - and nothing would have said so, because the
# application starts happily with either key.
# ------------------------------------------------------------------------------
cp "$GOOD_ENV_STAGING" "$FIXTURE/.env.staging"
sed -i 's|^QR_CIPHER_KEY=.*|QR_CIPHER_KEY=prod-only-key|' "$FIXTURE/.env.staging"
out=$(cd "$FIXTURE" && bash ./preflight.sh --env-only 2>&1); rc=$?
cp "$GOOD_ENV_STAGING" "$FIXTURE/.env.staging"
check "a shared QR cipher key is refused" 1 "$rc"
[[ "$out" == *"share the same QR_CIPHER_KEY"* ]] \
  && { echo "  PASS  the refusal says what is shared and what it costs"; pass=$((pass+1)); } \
  || { echo "  FAIL  the refusal does not name the shared key: $out"; fail=$((fail+1)); }

out=$(cd "$FIXTURE" && bash ./preflight.sh --env-only 2>&1); rc=$?
check "different QR cipher keys pass" 0 "$rc"
[[ "$out" == *"the QR cipher keys differ"* ]] \
  && { echo "  PASS  it says the keys were compared"; pass=$((pass+1)); } \
  || { echo "  FAIL  it does not say the keys were compared: $out"; fail=$((fail+1)); }

# ------------------------------------------------------------------------------
# The four secrets the env templates ship must not survive into the deployed file.
#
# The repository is public until 2026-11-01, so the template's values are on the internet. An
# operator who copies the template and misses a line deploys a platform whose access tokens are
# signed with a string anybody can read - and an HS256 token signed with a known string is a
# staff token anybody can forge. The application refuses the same placeholders at boot; this is
# the half that runs BEFORE the deploy.
# ------------------------------------------------------------------------------
for var in JWT_SECRET QR_CIPHER_KEY POSTGRES_PASSWORD REDIS_PASSWORD; do
  template_value="$(grep -E "^${var}=" "$SCRIPT_DIR/.env.production.example" | head -1 | cut -d= -f2-)"
  cp "$GOOD_ENV" "$FIXTURE/.env"
  sed -i "s|^${var}=.*|${var}=${template_value}|" "$FIXTURE/.env"
  out=$(cd "$FIXTURE" && bash ./preflight.sh --env-only 2>&1); rc=$?
  check "a $var that still holds the template's value is refused" 1 "$rc"
  [[ "$out" == *"$var"* ]] \
    && { echo "  PASS  the refusal names $var"; pass=$((pass+1)); } \
    || { echo "  FAIL  the refusal does not name $var: $out"; fail=$((fail+1)); }
  # The value is a placeholder, so printing it would leak nothing - but the check fires on a
  # value that merely READS like one, and the day it misfires on a real secret, printing it
  # would put that secret in the deploy log.
  [[ "$out" != *"$template_value"* ]] \
    && { echo "  PASS  the refusal does not print the value"; pass=$((pass+1)); } \
    || { echo "  FAIL  the refusal printed the value: $out"; fail=$((fail+1)); }
  cp "$GOOD_ENV" "$FIXTURE/.env"
done

# ------------------------------------------------------------------------------
# The pattern alone can be defeated by editing the template to hide the placeholder.
#
# "CHANGE_ME" is a word: a template that says something else sails past the pattern check. The
# verbatim comparison cannot be defeated that way, because the template's own values are what it
# compares against. So the check has to survive a template whose placeholder does not read like
# one - and the deployed value that came from it must still be refused.
# ------------------------------------------------------------------------------
sed -i 's|^JWT_SECRET=.*|JWT_SECRET=dummy-value-from-the-template|' \
  "$FIXTURE/.env.production.example"
cp "$GOOD_ENV" "$FIXTURE/.env"
sed -i 's|^JWT_SECRET=.*|JWT_SECRET=dummy-value-from-the-template|' "$FIXTURE/.env"
out=$(cd "$FIXTURE" && bash ./preflight.sh --env-only 2>&1); rc=$?
check "a value taken verbatim from an edited template is refused" 1 "$rc"
[[ "$out" == *"still holds the value from"* ]] \
  && { echo "  PASS  the refusal says the value came from the template"; pass=$((pass+1)); } \
  || { echo "  FAIL  the refusal does not name the template: $out"; fail=$((fail+1)); }
cp "$GOOD_ENV" "$FIXTURE/.env"
cp "$SCRIPT_DIR/.env.production.example" "$FIXTURE/.env.production.example"

# And a value that is neither a placeholder nor a template value passes - the check that refuses
# everything is not a check, it is an outage.
cp "$GOOD_ENV" "$FIXTURE/.env"
out=$(cd "$FIXTURE" && bash ./preflight.sh --env-only 2>&1); rc=$?
check "real secrets pass the placeholder check" 0 "$rc"
[[ "$out" == *"no env template is available here"* ]] \
  && { echo "  FAIL  the template was not found in the fixture"; fail=$((fail+1)); } \
  || { echo "  PASS  the template was found, so the verbatim check really ran"; pass=$((pass+1)); }

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

# ------------------------------------------------------------------------------
# The compose files must be proven to PARSE, not merely to exist.
#
# `QrCipher__Key` was added at eight spaces into a block whose siblings sit at six.
# go-yaml then refused the entire file, staging deploy failed, the chained production
# deploy was skipped, and main stayed red across two merges. Every C# test stayed
# green, and so did the preflight, because until now it only ever read the .env.
#
# The gate is worthless if it cannot fail, so assert that it fires - and that the
# message names the real cause rather than something invented.
#
# Skipped, loudly, where Compose cannot run at all. A skip here is not a green: the CI job runs on
# ubuntu-latest with Docker, so if this prints SKIP there, the gate is unverified and the run
# should be treated as unverified too.
# ------------------------------------------------------------------------------
if ! docker compose version >/dev/null 2>&1; then
  echo "  SKIP  docker compose cannot run here, so the compose parse gate is UNVERIFIED on this machine"
else
GOOD_COMPOSE="$FIXTURE/docker-compose.prod.yml"
cp "$SCRIPT_DIR/docker-compose.prod.yml" "$GOOD_COMPOSE"
out=$(cd "$FIXTURE" && bash ./preflight.sh --env-only 2>&1)
[[ "$out" == *"docker-compose.prod.yml: parses"* ]] \
  && { echo "  PASS  the parse gate reports a good compose file"; pass=$((pass+1)); } \
  || { echo "  FAIL  the parse gate did not confirm a file that is valid: $out"; fail=$((fail+1)); }

# A broken docker must not be reported as a broken compose file. This is the assertion that keeps
# the gate honest: `command -v docker` is satisfied by a WSL shim and an unreachable daemon, and a
# non-zero exit from either is indistinguishable from bad YAML unless the gate probes the binary.
# Shadow `docker` rather than clearing PATH - an empty PATH also hides `bash`, which fails for a
# reason that has nothing to do with what is being tested.
FAKE_DOCKER_BIN="$TMP/fake-docker-bin"
mkdir -p "$FAKE_DOCKER_BIN"
printf '#!/bin/sh\necho "Cannot connect to the Docker daemon at unix:///var/run/docker.sock." >&2\nexit 1\n' \
  > "$FAKE_DOCKER_BIN/docker"
chmod +x "$FAKE_DOCKER_BIN/docker"
out=$(cd "$FIXTURE" && PATH="$FAKE_DOCKER_BIN:$PATH" bash ./preflight.sh --env-only 2>&1); rc=$?
check "an unusable docker does not fail the preflight" 0 "$rc"
[[ "$out" != *"does not load"* ]] \
  && { echo "  PASS  an unusable docker is not blamed on the compose file"; pass=$((pass+1)); } \
  || { echo "  FAIL  the gate blamed the compose file for a docker that cannot run: $out"; fail=$((fail+1)); }
[[ "$out" == *"NOT parse-checked"* ]] \
  && { echo "  PASS  the gate says plainly that it did not check"; pass=$((pass+1)); } \
  || { echo "  FAIL  the gate did not admit it skipped the check: $out"; fail=$((fail+1)); }

# The real defect, reproduced exactly: one mapping line at the wrong indent.
printf 'services:\n  api:\n    image: x\n    environment:\n      GOOD: "1"\n        BAD: "2"\n' \
  > "$FIXTURE/docker-compose.prod.yml"
out=$(cd "$FIXTURE" && bash ./preflight.sh --env-only 2>&1); rc=$?
check "a misindented compose file fails the preflight" 1 "$rc"
[[ "$out" == *"docker-compose.prod.yml: parses"* ]] \
  && { echo "  FAIL  a file go-yaml rejects was reported as parsing"; fail=$((fail+1)); } \
  || { echo "  PASS  a file go-yaml rejects is not reported as parsing"; pass=$((pass+1)); }
# "indented", not "indentation" - the message says which mistake to look for, and an assertion
# that did not match the message it was written against would have been a false green.
[[ "$out" == *"indented"* ]] \
  && { echo "  PASS  the failure points at the cause (a wrongly indented line)"; pass=$((pass+1)); } \
  || { echo "  FAIL  the failure does not mention an indented line: $out"; fail=$((fail+1)); }
# Restore it, or the remaining assertions below inherit a broken file.
cp "$SCRIPT_DIR/docker-compose.prod.yml" "$GOOD_COMPOSE"
fi

echo
echo "  $pass passed, $fail failed"
[[ $fail -eq 0 ]]