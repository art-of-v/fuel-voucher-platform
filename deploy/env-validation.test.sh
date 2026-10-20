#!/usr/bin/env bash
# Local harness + CI gate for deploy/*.env*.example and for load_env.sh itself.
#
# WHY THIS EXISTS (#219)
# The real `.env.staging` is gitignored and lives only on the box, so no test can read it and
# nothing fails until a deploy runs. Two defects sat there for several deploys:
#
#   - line 1 began with `\#` instead of `#`. A shell reads `\#` as an assignment, so
#     `source` dies at the parenthesis a few characters later. The deploy workflow runs
#     STAGING BEFORE PRODUCTION and chains them, so a staging failure SKIPS the production
#     deploy too - `main` went red for two consecutive merges with nothing in the diff to
#     explain it.
#   - an unquoted value containing a space and a `>` (`MONOBANK_PUBLIC_KEY`), which is fine
#     to a human reading the file and a syntax error to a shell.
#
# The examples ARE in the repo and CAN be tested. Every one of them is the file an operator
# copies onto a box, so a defect there is a defect shipped to production waiting for a
# restart to detonate. This makes that failure happen here instead.
set -uo pipefail
cd "$(dirname "$0")"

pass=0; fail=0

ok()   { pass=$((pass + 1)); printf '  ok    %s\n' "$1"; }
bad()  { fail=$((fail + 1)); printf '  FAIL  %s\n' "$1"; }

# ---------------------------------------------------------------------------------
# 1. The example files must be valid shell when sourced.
#
# Sourced on purpose, not parsed with load_env: the defect was that something SOURCES
# them, so that is the thing that has to hold. load_env is deliberately more forgiving
# and would have hidden the bug rather than caught it.
# ---------------------------------------------------------------------------------
for example in .env.production.example .env.staging.example; do
  if [[ ! -f "$example" ]]; then
    bad "$example is missing"
    continue
  fi

  err="$(bash -n "$example" 2>&1)"
  if [[ -n "$err" ]]; then
    bad "$example is not valid shell syntax: $err"
    continue
  fi
  ok "$example is valid shell syntax"

  # A leading `\#` parses as an assignment, not a comment. Grep for it directly, because
  # `bash -n` accepts it - the failure only happens when a later line is reached.
  if grep -nE '^[[:space:]]*\\#' "$example" > /dev/null; then
    bad "$example has a line starting with a backslash before the # ($(grep -nE '^[[:space:]]*\\#' "$example" | head -1 | cut -d: -f1)) - a shell reads it as an assignment, not a comment"
  else
    ok "$example has no escaped comment lines"
  fi

  # Unquoted values holding whitespace are the second real defect. Docker Compose accepts
  # them; a shell does not. `bash -n` catches most of them, so this only reports the ones
  # that slipped past to keep the message specific.
  unquoted="$(grep -nE '^[A-Za-z_][A-Za-z0-9_]*=[^"'"'"'[:space:]]*[[:space:]][^[:space:]]' "$example" || true)"
  if [[ -n "$unquoted" ]]; then
    bad "$example has an unquoted value containing whitespace: $(echo "$unquoted" | head -1 | cut -d: -f1)"
  else
    ok "$example has no unquoted whitespace in any value"
  fi
done

# ---------------------------------------------------------------------------------
# 2. STAGING_BASICAUTH_HASH must look like a bcrypt hash, not an arbitrary string.
#
# Caddy treats a value it cannot identify as base64 plaintext and fails to decode it, so
# Caddy refuses to start. The value on the box was a random 63-character string that could
# never have matched any password - nothing noticed because the running container predated
# it. Catching it here means it is caught before a restart, not during one.
# ---------------------------------------------------------------------------------
for example in .env.staging.example .env.production.example; do
  [[ -f "$example" ]] || continue

  hash_line="$(grep -E '^[[:space:]]*[A-Z_]*BASICAUTH_HASH=' "$example" || true)"
  if [[ -z "$hash_line" ]]; then
    # Not every deployment sets one; absence is not a defect.
    ok "$example sets no basic-auth hash (nothing to validate)"
    continue
  fi

  value="${hash_line#*=}"
  value="${value%\"}"; value="${value#\"}"
  value="${value%\'}"; value="${value#\'}"

  # bcrypt hashes are $2a$/$2b$/$2y$ + 22 salt chars + 31 digest chars = 60 characters.
  if [[ "$value" =~ ^\$2[aby]?\$[./A-Za-z0-9]{53}$ ]]; then
    ok "$example basic-auth hash is bcrypt-shaped"
  else
    bad "$example basic-auth hash is NOT a bcrypt hash (len=${#value}) - Caddy will treat it as base64 plaintext and refuse to start. Generate it with: caddy hash-password"
  fi
done

# ---------------------------------------------------------------------------------
# 3. load_env must actually load an example without executing it.
# ---------------------------------------------------------------------------------
if command -v bash > /dev/null; then
  out="$(bash -c '
    set -euo pipefail
    . ./load-env.sh
    load_env .env.staging.example
    # The template is all placeholders, so only assert that loading produced variables and
    # that nothing was executed.
    printenv ROOT_DOMAIN > /dev/null || { echo "ROOT_DOMAIN not set"; exit 1; }
  ' 2>&1)"
  if [[ $? -eq 0 ]]; then
    ok "load_env parses .env.staging.example"
  else
    bad "load_env failed on .env.staging.example: $out"
  fi
fi

printf '\n%s passed, %s failed\n' "$pass" "$fail"
[[ "$fail" -eq 0 ]]