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
#   1. Env files that Docker Compose can actually read, and whose secrets are not
#      still the values the env template ships
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

PROD_COMPOSE=(docker compose --env-file .env.production -f docker-compose.prod.yml)

warnings=()
fail() { echo "ERROR: $*" >&2; exit 1; }
warn() { echo "WARNING: $*" >&2; warnings+=("$*"); }

# The compose service names and the container names are not derivable from each
# other - `admin-frontend` runs as `fuelflow-admin` and `website-frontend` as
# `fuelflow-website`. The first version of this script guessed with a substitution,
# looked up `fuelflow-fuelflow-backend`, found nothing, and reported "(not running)"
# for all three while passing cleanly - which is the worst possible failure for a
# check whose whole job is to notice that the running image is not what a tagless
# `up -d` would pick. Spelled out, and exercised against the real box.
service_container() {
  case "$1" in
    fuelflow-backend)  echo "fuelflow-backend" ;;
    admin-frontend)  echo "fuelflow-admin" ;;
    website-frontend) echo "fuelflow-website" ;;
    *) echo "fuelflow-$1" ;;
  esac
}

# --env-only stops after step 1 and --env-file checks an extra path, so the env
# scan can be exercised against a known-broken file instead of trusted.
ENV_ONLY=0
DATA_ONLY=0
NO_DEFAULTS=0
EXTRA_ENV_FILES=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --env-only) ENV_ONLY=1; shift ;;
    --data-only) DATA_ONLY=1; shift ;;
    # --no-defaults skips .env.production/.env.staging so the scan can be exercised against a
    # fixture; on the box the real files are always wanted.
    --no-defaults) NO_DEFAULTS=1; shift ;;
    --env-file) EXTRA_ENV_FILES+=("${2:?--env-file needs a path}"); shift 2 ;;
    -h|--help) sed -n '2,22p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

# ------------------------------------------------------------------------------
# 5. Data preconditions
#
# Everything above inspects configuration. This is the only check that looks at the DATA, and it
# exists because of what happened on 2026-10-15.
#
# The offer split's migration refused to apply because two OKKO fuel types carry different per-litre
# prices across their volumes. Refusing was right - it cannot know which copy is true, and picking
# one would have published an unverified price. But the refusal happened at container start, so
# production was DOWN before the message was read, and staging could not catch it first because
# staging has no imported fuel types to drift.
#
# The same assertion asked here, read-only, before anything is pulled, is a thirty-second answer
# instead of an outage. The migration's guard stays - data can change between this check and the
# boot, and it must still refuse rather than guess.
#
# WARNINGS, not failures, and that is a deliberate temporary state: production currently violates
# 010, so a blocking check would stop every deploy behind one unresolved group. Flip
# PREFLIGHT_DB_STRICT comes from the environment of this process, NOT from .env.production. The script passes
# --env-file to docker compose and never sources anything into its own shell, so writing it to
# deploy/.env.production has no effect on this line. The workflow step that runs this sets it from a repository
# variable; see preflight-db/README.md.
#
# Defined with the other checks rather than inline at the bottom, because --data-only calls it
# before step 1 has run. Returns 1 only in strict mode, so that flag is testable on its own.
# ------------------------------------------------------------------------------
check_data_preconditions() {
  echo "== data preconditions =="
  local violated=0 checked=0 unreadable=0 sql name rows psql_out psql_status
  # The names, not just the count. A blocking message that says "1 precondition violated" sends
  # someone hunting through the log for which file; the file is already known here and is the one
  # thing worth reading, because its header explains what the condition means and what to do.
  local -a violated_files=() unreadable_files=()

  if [[ ! -d preflight-db ]]; then
    echo "  no preflight-db directory (nothing to check)"
    echo "Preflight passed."
    return 0
  fi

  for sql in preflight-db/*.sql; do
    [[ -e "$sql" ]] || continue
    name="$(basename "$sql")"

    # The database container is not necessarily running during a CI --env-only run, and a
    # precondition that cannot reach the database must say so rather than pass in silence: a check
    # that reports nothing when it did not run is the same failure as the Caddy upstream check that
    # "passed cleanly" while matching nothing.
    if ! docker ps --format '{{.Names}}' 2>/dev/null | grep -qx 'fuelflow-postgres'; then
      warn "$name: not checked - the database is not reachable from here"
      continue
    fi

    # Count lines carrying the field separator, NOT non-empty lines. psql writes its own output to
    # stdout - "BEGIN", "SET", "(0 rows)", "COMMIT" - and counting those made this report
    # "VIOLATED - 3 row(s)" against a completely clean production database, then print "(0 rows)"
    # as the detail underneath. It shipped that way and fired on the first real deploy.
    #
    # A gate that fires on a healthy system is worse than no gate, because it is a gate people
    # learn to skip. Every row this query emits is pipe-separated, because the runner asks psql for
    # -F'|'; psql's own chatter contains no pipe.
    #
    # ON_ERROR_STOP=1 AND THE OUTPUT ARE BOTH NEEDED, and they are one fix rather than two.
    # `2>/dev/null | grep -c '|'` counted an aborted query as zero rows, because a SQL error
    # produces no stdout and psql exits 0 anyway: `011` grouped by `f.station_id` for a week after
    # that column was renamed to `brand_id`, and every deploy printed
    # "011-fuel-type-name-twins.sql: ok" over a query PostgreSQL had refused. The two halves are
    # what make that impossible: the setting is what makes psql exit non-zero on a SQL error, and
    # capturing it is what lets the exit code be seen at all instead of being thrown away with the
    # stderr. A check that cannot run is now reported as one that did not run.
    psql_status=0
    psql_out="$(docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -X -t -A -F'|' \
      -v ON_ERROR_STOP=1 -f - < "$sql" 2>&1)" || psql_status=$?
    if [[ "$psql_status" -ne 0 ]]; then
      unreadable=$((unreadable + 1))
      unreadable_files+=("$name")
      # The first ERROR line is the whole answer; the rest of psql's chatter is noise.
      warn "$name: COULD NOT RUN - the query failed (psql exit ${psql_status}), so nothing was checked: $(grep -m1 -E '^(psql:)?ERROR' <<<"$psql_out" || echo 'psql reported no error line')"
      continue
    fi

    checked=$((checked + 1))
    rows="$(grep -c '|' <<<"$psql_out" || true)"
    if [[ "${rows:-0}" -eq 0 ]]; then
      echo "  $name: ok"
    else
      violated=$((violated + 1))
      violated_files+=("$name")
      warn "$name: VIOLATED - ${rows} row(s). Details:"
      docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -X -f - < "$sql" 2>&1 | sed 's/^/      /' >&2
    fi
  done

  # "satisfied" is only true of something that was actually asked. The first version of this check
  # printed it whenever nothing was violated - including when the database was unreachable and no
  # query had run at all - which is precisely the failure this file exists to stop: silence read as
  # a clean system. A check that cannot tell "clean" from "did not run" is worse than none.
  #
  # "did not run" has two shapes, and both count here: the database was unreachable (nothing
  # checked), and a query PostgreSQL refused (that one file, the rest still answered). The second
  # is the one that shipped silently - a check naming a column that no longer exists reported
  # "ok" for a week. So a file that could not run is excluded from the satisfied line rather than
  # counted into it.
  if [[ $unreadable -gt 0 ]]; then
    echo "  ${unreadable} precondition(s) could NOT be run: ${unreadable_files[*]}"
  fi
  if [[ $violated -eq 0 && $checked -gt 0 ]]; then
    echo "  all data preconditions that ran are satisfied (${checked} checked)"
  elif [[ $checked -eq 0 ]]; then
    echo "  NO precondition was checked - see the warnings above"
  fi

  # Tested before the summary, because "Preflight passed" followed by an exit 1 is a contradiction
  # on the same screen, and the last line a reader saw is the one they remember.
  if [[ "${PREFLIGHT_DB_STRICT:-0}" == "1" && "$violated" -gt 0 ]]; then
    fail "PREFLIGHT_DB_STRICT=1 and ${violated} data precondition(s) are violated: ${violated_files[*]}. Nothing was pulled. Each file's header says what the condition means and what to do about it; re-read them before touching the data."
  fi

  if [[ ${#warnings[@]} -gt 0 ]]; then
    echo "Preflight passed with ${#warnings[@]} warning(s). Review them before restarting."
  else
    echo "Preflight passed."
  fi
  return 0
}
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

# ------------------------------------------------------------------------------
# The five secrets the env templates ship must not survive into the deployed file.
#
# deploy/.env.production.example ships five placeholders:
#
#   POSTGRES_PASSWORD=CHANGE_ME_openssl_rand_base64_36
#   REDIS_PASSWORD=CHANGE_ME_openssl_rand_hex_32
#   JWT_SECRET=CHANGE_ME_at_least_32_characters_long_random_string
#   QR_CIPHER_KEY=CHANGE_ME_openssl_rand_base64_32
#   OTP_PEPPER=CHANGE_ME_openssl_rand_base64_32
#
# They are INSTRUCTIONS, not secrets - and the repository is public until 2026-11-01, so
# they are on the internet. An operator who copies the template and misses a line deploys
# a platform whose access tokens are signed with a string anybody can read: the JWT key
# signs an HS256 token, so anyone can forge a staff token with it. That is full staff
# console access - refunds, payment placement, customer PII, the redeemable voucher
# payloads - and the whole session model (rotation, reuse detection, device binding) is
# bypassed, because the forger never holds a refresh token.
#
# Two checks, because either one alone has a hole:
#
#   - the PATTERN. Any value that reads like a placeholder is refused, whatever wording it
#     uses. The application refuses the same pattern at boot (SharedKernel/Security/
#     PlaceholderSecret.cs), so the two agree on what a placeholder is.
#   - the TEMPLATE'S OWN VALUES, verbatim. The pattern can be defeated by editing the
#     template to hide the placeholder - "CHANGE_ME" is a word, and a template that says
#     "REPLACE_THIS" would sail past it. Comparing against the template's values cannot be
#     defeated that way: the template's own values are what it compares against.
#
# The failure never prints the value. The check fires on a value that is either a
# placeholder or the template's own - both public - but a check that prints the value it
# found would print a REAL secret into the deploy log the day it misfires.
#
# A literal scan, like the env-file check above: sourcing the file would execute it, and
# these files hold secrets.
# ------------------------------------------------------------------------------
check_placeholder_secrets() {
  local file="$1"; shift
  local templates=("$@")
  local var value template template_value template_present=0

  for template in ${templates[@]+"${templates[@]}"}; do
    if [[ -f "$template" ]]; then template_present=1; fi
  done

  if [[ $template_present -eq 0 ]]; then
    warn "$file: no env template is available here, so the verbatim template check could not run (the placeholder pattern is still checked)"
  fi

  # The database has more than one password since the four-role layout: the superuser's, the
  # traffic role's, the migrator's and the maintenance role's. All of them are secrets, so all
  # of them are checked - and a variable the env file does not carry is skipped, so the list can
  # name a role that a given deployment does not use.
  for var in JWT_SECRET OTP_PEPPER QR_CIPHER_KEY POSTGRES_PASSWORD POSTGRES_APP_PASSWORD POSTGRES_MIGRATOR_PASSWORD POSTGRES_MAINTENANCE_PASSWORD POSTGRES_READONLY_PASSWORD REDIS_PASSWORD; do
    value="$(grep -E "^${var}=" "$file" | head -1 | cut -d= -f2- || true)"
    if [[ -z "$value" ]]; then
      continue
    fi

    if [[ "${value,,}" =~ (change[_-]?me|replace[_-]?me|insert[_-]?here|placeholder|example|your[_-]) ]]; then
      fail "$file: $var still holds a placeholder value. The env templates ship placeholder values because they are instructions, not secrets - and they are published in a public repository, so a value that was never replaced is a value everybody has. The JWT key signs the access tokens, so anyone who reads it can forge a staff token. Generate a real one (openssl rand -base64 36 for the JWT key and the database password, openssl rand -hex 32 for Redis, openssl rand -base64 32 for the QR cipher key) and put it in $file."
    fi

    for template in ${templates[@]+"${templates[@]}"}; do
      [[ -f "$template" ]] || continue
      template_value="$(grep -E "^${var}=" "$template" | head -1 | cut -d= -f2- || true)"
      if [[ -n "$template_value" && "$value" == "$template_value" ]]; then
        fail "$file: $var still holds the value from $template. The pattern check cannot see this one - the template's value does not read like a placeholder - but the value is published in a public repository all the same, and a JWT key that everybody has is a JWT key that anybody can sign a staff token with. Generate a real one and put it in $file."
      fi
    done
  done
}

# ------------------------------------------------------------------------------
# A guardrail switch on the box must be the one the repository ships.
#
# A git deploy does not touch the droplet's env file: it is gitignored and lives only
# on the box, so nothing ever compared it with the checked-in example. That is #746:
# ATTESTATION_ENABLED was false in production while the template, the compose file and
# the docs all said true. The API measured nothing for days, no test failed, and the
# only way anybody found out was a campaign that expected a log line and did not get
# one. Any live-only switch can drift that way - the failure is silent by construction.
#
# The keys below are the ones where a different value on the box is a decision rather
# than a configuration: the guardrails. Secrets and host-specific values (passwords,
# URLs, the QR cipher key) are deliberately NOT here - they are meant to differ, and a
# check that fires on them is a check the operator learns to ignore.
#
# A deliberate divergence is allowed, but it has to be written down in the file it is
# about: ENV_DRIFT_ACKNOWLEDGED="KEY1 KEY2" in the live file turns those keys'
# mismatches into a warning that names them on every restart. The acknowledgement and
# the diverging value are then one edit, and both are in the same diff.
#
# A key that is ABSENT from the box file is not a divergence: the compose file defaults
# every one of these (${ATTESTATION_ENABLED:-true}), and the defaults equal the example,
# so the container runs what the repository ships. Failing on absence would block a box
# whose runtime is correct, which is how a check earns the right to be ignored. It warns
# instead - the file no longer records what the container runs with.
#
# Compared with <file>.example - which is exactly what the checked-in templates are
# named (.env.production.example sits beside .env.production), so the same rule covers
# the two box files and an extra --env-file a CI run stands in with.
# ------------------------------------------------------------------------------
GUARDED_SWITCHES=(
  ATTESTATION_ENABLED
  ATTESTATION_MODE
  ATTESTATION_ACKNOWLEDGE_DISABLED_IN_PRODUCTION
  VOUCHER_EXPIRATION_ENABLED
)

# shellcheck disable=SC2317 # every call is from the env section below
check_switches_match_example() {
  local file="$1" example="$1.example"
  local key expected actual acknowledged compared=0 problems=()

  [[ -f "$file" ]] || return 0
  if [[ ! -f "$example" ]]; then
    warn "$file: no $example beside it, so the guardrail switches were not compared with the repository"
    return 0
  fi

  acknowledged=" $(grep -E '^ENV_DRIFT_ACKNOWLEDGED=' "$file" | head -1 | cut -d= -f2- | tr -d '\r"' || true) "

  for key in "${GUARDED_SWITCHES[@]}"; do
    expected="$(grep -E "^${key}=" "$example" | head -1 | cut -d= -f2- | tr -d '\r' || true)"
    # A tier's example does not have to ship every switch - staging has no attestation -
    # and a blank value in the example is an empty slot, not an expectation.
    if [[ -z "$expected" ]]; then
      continue
    fi

    if ! grep -qE "^${key}=" "$file"; then
      warn "$file: $key is not set at all, so the container takes the compose default (the example ships '$expected'). Add the line so the box records what it runs with."
      continue
    fi

    actual="$(grep -E "^${key}=" "$file" | head -1 | cut -d= -f2- | tr -d '\r' || true)"
    compared=$((compared + 1))
    if [[ "$actual" == "$expected" ]]; then
      continue
    fi

    if [[ "$acknowledged" == *" $key "* ]]; then
      warn "$file: $key is '$actual' here and '$expected' in $example - acknowledged by ENV_DRIFT_ACKNOWLEDGED"
    else
      problems+=("$key is '$actual' here and '$expected' in $example")
    fi
  done

  if [[ ${#problems[@]} -gt 0 ]]; then
    fail "$file disagrees with $example on a guardrail switch:
$(printf '    - %s\n' "${problems[@]}")  A git deploy never updates this file, so the box keeps the old value until somebody sets it - and whatever the switch guards stays off while the repository, the compose file and the docs all say on. Set the key(s) in $file to the example's value, or record the difference as ENV_DRIFT_ACKNOWLEDGED=\"KEY\" in the same file if it is deliberate. Nothing has been restarted."
  fi

  if [[ "$compared" -gt 0 ]]; then
    echo "  $file: the guardrail switches match $example ($compared compared)"
  fi
}

# ------------------------------------------------------------------------------
# The two environments must not share the voucher QR cipher key.
#
# The key encrypts the redeemable QR payload of every voucher at rest, and it is
# the ONLY thing that can read it. Until 2026-10-21 staging used production's
# key, so anyone who reached staging - behind an HTTP Basic credential, on the
# same host - could decrypt production's vouchers. The keys are separate now.
#
# The check is here because the failure mode is a copy-paste of one value into
# the other file, and nothing else would notice: the application starts happily
# with either key, and a voucher that cannot be decrypted shows up much later as
# an operator staring at a QR that will not scan.
#
# A literal scan, like the env-file check above: sourcing the file would execute
# it, and these files hold secrets.
# ------------------------------------------------------------------------------
check_qr_key_separation() {
  local prod_file=".env.production" staging_file=".env.staging"

  if [[ ! -f "$prod_file" || ! -f "$staging_file" ]]; then
    warn "one of the two env files is missing (skipping the QR cipher key separation check)"
    return 0
  fi

  local prod_key staging_key
  prod_key="$(grep -E '^QR_CIPHER_KEY=' "$prod_file" | head -1 | cut -d= -f2-)"
  staging_key="$(grep -E '^QR_CIPHER_KEY=' "$staging_file" | head -1 | cut -d= -f2-)"

  if [[ -z "$prod_key" || -z "$staging_key" ]]; then
    warn "one of the two env files has no QR_CIPHER_KEY (skipping the separation check)"
    return 0
  fi

  if [[ "$prod_key" == "$staging_key" ]]; then
    fail "production and staging share the same QR_CIPHER_KEY. It encrypts every voucher's redeemable payload, so anyone who reaches staging can decrypt production's vouchers. Generate a different one for staging (openssl rand -base64 32) and put it in .env.staging - the two are not interchangeable, and the staging database holds no vouchers that would need re-encrypting."
  fi

  echo "  .env.production and .env.staging: the QR cipher keys differ"
}

# ------------------------------------------------------------------------------
# The chat id the alert scripts use and the chat id the Grafana provisioning
# ships must be the same number.
#
# They live in two places on purpose. The provisioning file carries it as a
# LITERAL, because Grafana substitutes $VAR into that YAML and parses the result
# again: a chat id that arrives as a variable is a YAML NUMBER, and Grafana's
# Telegram integration refuses to start on it ("cannot unmarshal number into Go
# struct field Config.chatid of type string") - which takes the whole alerting
# provisioning down, rules included, and leaves Grafana restart-looping.
# deploy/.env.production carries the same number for the systemd scripts that send the
# backup and disk alerts.
#
# Two places, one fact, so this check says whether they still agree. Without it
# the alerting would quietly split in two: Grafana's alerts in one group, the
# scripts' in another, and neither place complete.
# ------------------------------------------------------------------------------
check_telegram_chat_id() {
  local file="../backend/observability/grafana/provisioning/alerting-telegram/contact-points.yml"
  local env_file=".env.production"

  if [[ ! -f "$file" ]]; then
    warn "$file is missing (skipping the Telegram chat id check)"
    return 0
  fi
  if [[ ! -f "$env_file" ]]; then
    warn "$env_file is missing (skipping the Telegram chat id check)"
    return 0
  fi

  local shipped env_value
  shipped="$(grep -E '^[[:space:]]+chatid:' "$file" | head -1 \
    | sed -E 's/.*chatid:[[:space:]]*//' | tr -d '"'"'"'' | tr -d ' ')"
  env_value="$(grep -E '^TELEGRAM_CHAT_ID=' "$env_file" | head -1 \
    | cut -d= -f2- | tr -d '"'"'"'' | tr -d ' ')"

  if [[ -z "$shipped" || -z "$env_value" ]]; then
    warn "no chat id in one of the two places (skipping the Telegram chat id check)"
    return 0
  fi

  if [[ "$shipped" != "$env_value" ]]; then
    fail "the Telegram chat id in $file ($shipped) is not the one in $env_file ($env_value). Grafana sends the metric alerts to the first and the systemd scripts to the second, so the alerts would arrive in two different groups - and the one that says 'the app is down' would arrive in neither."
  fi

  echo "  the chat id in $file and $env_file agree"
}

# ------------------------------------------------------------------------------
# The staging basic-auth hash reaches caddy as a well-formed bcrypt hash.
#
# `caddy validate` above catches a value that is not a hash at all - it reads one
# it cannot recognise as base64-encoded plaintext and fails:
#
#   base64-decoding password: illegal base64 data at input byte 76
#
# which is exactly the state the box was in: a random 63-character string, and a
# staging site that had never had a working credential.
#
# What compose does to the value matters here. Measured on the box: .env.production held 63
# characters with SIX `$` and caddy received 60 with THREE, so compose collapses
# `$$` to `$` and passes a lone `$` through unchanged.
#
# That has a consequence worth stating because the opposite is easy to believe:
# **partially escaping the `$` is harmless.** A four-dollar value still collapses
# to a well-formed hash and still works. The Caddyfile's "double every `$`" rule is
# true, but the failure is not a partially escaped hash - it is a value that is not
# a hash. So this checks the thing that actually breaks caddy: what caddy will
# receive once compose has had its way with it.
#
# It runs against deploy/.env.production ONLY, and only because that is where caddy reads it
# from - caddy is the production service, and STAGING_BASICAUTH_* means "the
# staging vhosts prod's caddy fronts", not "a variable the staging backend owns".
# Scanning .env.staging for it produced a warning that was simply false: staging
# was gated and working while preflight reported it inert, every run, to whoever
# was reading. A check that cries wolf on a working deploy is how a gate stops
# being read.
# ------------------------------------------------------------------------------
check_basic_auth_hash() {
  local file="$1" raw collapsed

  raw=$(grep -E '^STAGING_BASICAUTH_HASH=' "$file" | head -1 | cut -d= -f2- || true)
  # Strip one layer of surrounding quotes, the way compose does.
  if [[ ${#raw} -ge 2 && "$raw" == \"*\" ]]; then raw="${raw:1:${#raw}-2}"
  elif [[ ${#raw} -ge 2 && "$raw" == \'*\' ]]; then raw="${raw:1:${#raw}-2}"; fi

  if [[ -z "$raw" ]]; then
    warn "$file: STAGING_BASICAUTH_HASH not set; staging basic auth stays inert until it is (documented in the Caddyfile)"
    return 0
  fi

  # Exactly what compose does to the value on its way into the container.
  collapsed=$(printf '%s' "$raw" | sed 's/\$\$/$/g')

  if [[ "$collapsed" =~ ^\$2[aby]\$[0-9]{2}\$[./A-Za-z0-9]{53}$ ]]; then
    echo "  $file: STAGING_BASICAUTH_HASH reaches caddy as a well-formed bcrypt hash"
    return 0
  fi

  fail "$file: STAGING_BASICAUTH_HASH is not a bcrypt hash once compose collapses \$\$, so caddy will read it as base64-encoded plaintext and REFUSE TO START - taking prod's only public ingress with it. Generate it with 'caddy hash-password'."
}

# --data-only runs step 5 and nothing else, for the same reason --env-only exists: steps 2 to 4
# need the real box, so a step-5 test would otherwise have to stand up Caddy and three containers to
# reach the only part that is cheap to exercise. Placed here, below the function it calls - bash has
# to have seen the definition before the call executes, or it reports "command not found".
if [[ "$DATA_ONLY" -eq 1 ]]; then
  check_data_preconditions
  exit $?
fi

echo "== env files =="
if [[ "$NO_DEFAULTS" -eq 0 ]]; then
  check_env_file .env.production required
  check_env_file .env.staging optional
  # The two environments must not share the voucher QR cipher key - see the function.
  check_qr_key_separation
  # The four secrets the templates ship must not have survived into the deployed files.
  check_placeholder_secrets .env.production .env.production.example .env.staging.example
  check_placeholder_secrets .env.staging .env.production.example .env.staging.example
  # The guardrail switches must be the ones the repository ships (#746, #753).
  check_switches_match_example .env.production
  check_switches_match_example .env.staging
fi
for extra in ${EXTRA_ENV_FILES+"${EXTRA_ENV_FILES[@]}"}; do
  check_env_file "$extra" optional
  check_placeholder_secrets "$extra" .env.production.example .env.staging.example
  check_switches_match_example "$extra"
done

# One credential check, on the one file caddy actually reads it from. The check
# used to be called once per env file AND again in a hand-copied block further
# down; #994 corrected the first copy's comment and the second kept the old,
# disproved wording. Two copies of one rule drift apart exactly like that.
echo "== caddy credentials =="
if [[ "$NO_DEFAULTS" -eq 0 ]]; then
  [[ -f .env.production ]] && check_basic_auth_hash .env.production
else
  # A CI --env-file run is a stand-in for deploy/.env.production, so validate it there too.
  for extra in ${EXTRA_ENV_FILES+"${EXTRA_ENV_FILES[@]}"}; do
    check_basic_auth_hash "$extra"
  done
fi

# ------------------------------------------------------------------------------
# The compose files actually parse
#
# check_env_file proves the .env.production is well-formed. It says nothing about the YAML, and
# the YAML is where the 2026-10-17 deploy died: `QrCipher__Key` was added at eight
# spaces into a block whose siblings sit at six, and go-yaml rejected the whole file
# with "did not find expected key". Staging failed, so the chained production deploy
# was skipped, and main sat red for two merges.
#
# Nothing upstream could catch it. Every unit and integration test passed - they
# compile C#, they never read the compose files - and this script passed too, because
# checking an env file is not the same thing as parsing the compose file that consumes
# it. The step is named "Check the box env files are readable by Compose" and passed
# immediately before Compose failed on the very next step, which is the clearest
# statement of the gap this closes.
#
# `config --quiet` renders and validates without touching a container or a network, so
# it is safe in --env-only and costs a second.
#
# The verdict is the EXIT CODE, not the message. Compose v5 exits 1 on an unparseable
# file while printing nothing at all, and prints a go-yaml error on other builds, so
# keying on the text makes the gate work on the box and silently pass in CI. Compose
# also exits 1 for an unset `${VAR:?}`, which is why every required variable has to be
# satisfied before a non-zero exit can be read as "did not load" - and it must be,
# because an unset required variable is just as fatal to `up -d`.
# ------------------------------------------------------------------------------
check_compose_parses() {
  local file="$1" env_file="$2" out rc=0
  # `command -v docker` is not enough. A WSL shim, an installed client with an unreachable daemon,
  # and a rootless setup all satisfy it and then fail on first use - and a non-zero exit from those
  # is indistinguishable from "this YAML is broken". Probing the binary catches all three, and the
  # difference matters: the first case means the gate could not run, the second means the file is
  # bad. Reporting a docker problem as a broken compose file sends whoever reads it to fix the wrong
  # file, which is the specific failure this gate exists to prevent.
  if ! docker compose version >/dev/null 2>&1; then
    warn "docker compose could not run here (not installed, or the daemon is unreachable), so $file was NOT parse-checked. Every deploy step needs Compose; fix that before trusting a green preflight."
    return 0
  fi
  if [[ ! -f "$file" ]]; then
    fail "$file is missing - the deploy would fail, but Compose would say so later and less clearly."
  fi
  out=$(docker compose --env-file "$env_file" -f "$file" config --quiet 2>&1) || rc=$?
  if [[ $rc -ne 0 ]]; then
    fail "$file does not load (compose exited $rc): ${out:-compose printed no message}. Compose cannot build its model from this file at all, so 'docker compose up' fails before a single container is touched - and because staging deploys before production, that skips production too. A mapping line indented to the wrong depth does exactly this: it is not a value, so only the YAML parser notices, and it takes the whole file with it."
  fi
  echo "  $file: parses"
}

# Part of the defaults scan, not of the targeted one. --no-defaults means "check only the files I
# named", and it skips .env/.env.staging - so resolving ${VAR:?} here would fail against a .env.production
# this run never claimed to look at, and the gate would report a broken compose file for a run that
# never intended to deploy it.
if [[ "$NO_DEFAULTS" -eq 0 ]]; then
  echo "== compose files =="
  check_compose_parses docker-compose.prod.yml .env.production
  check_compose_parses docker-compose.staging.yml .env.staging
fi

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
if ! docker compose --env-file .env.production -f docker-compose.prod.yml run --rm --no-deps \
      --entrypoint caddy caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null 2>&1; then
  echo "  (re-running with output)" >&2
  docker compose --env-file .env.production -f docker-compose.prod.yml run --rm --no-deps \
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
# same service-name aliases, so before app#977 the name `fuelflow-backend` resolved
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
    count=$(printf '%s\n' $owners | grep -c '|' || true)
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
# 3b. The names the alerting chain uses
#
# The Caddy upstreams above are the names the TRAFFIC uses. These are the names the
# ALERTING uses, and on 2026-10-20 they were wrong: the Prometheus scrape target named
# `dotnet-backend`, which is not a service anybody ran, so Prometheus scraped nothing from
# the API and every rule that reads a metric the API emits was an `absent()` that could
# never fire - including the one that says "customers may have paid without receiving
# vouchers".
#
# The fix landed as one string, and nothing checked it. This is the second time a rename
# has silently broken a name in this repository, so the check lives here: before anything
# is pulled, recreated or restarted, the names are resolved against the compose services
# that exist.
#
# The check is its own script because it must also run in CI, and it needs no container,
# no box and no .env.production - the compose service names are read out of the compose file itself.
# ------------------------------------------------------------------------------
echo "== the alerting chain's names =="
if ! bash "$SCRIPT_DIR/check-alerting-names.sh"; then
  fail "the alerting chain names something that does not resolve. Nothing has been restarted, and a Prometheus target or a Grafana datasource that points at a name nobody runs is an alerting chain that cannot fire."
fi

# ------------------------------------------------------------------------------
# The provisioning must LOAD, and the two chat ids must agree.
#
# The names above say the chain points at things that exist. These two say the
# chain is switched on at all: on 2026-10-22 the production Grafana had neither
# the Telegram overlay nor a single alerting rule, so the nine hours the API was
# down produced no alert at all - and the one alert that did arrive (the nightly
# backup) comes from systemd, not from Grafana.
# ------------------------------------------------------------------------------
echo "== the alerting provisioning =="
check_telegram_chat_id
if ! bash "$SCRIPT_DIR/check-alerting-provisioning.sh"; then
  fail "the Grafana alerting provisioning does not load. Nothing has been restarted, and a Grafana that cannot provision its contact point takes the rules with it - which is an alerting chain that is switched off while it looks switched on."
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
for svc in fuelflow-backend admin-frontend website-frontend; do
  ctr="$(service_container "$svc")"
  running=$(docker inspect "$ctr" --format '{{.Config.Image}}' 2>/dev/null || true)
  if [[ -z "$running" ]]; then
    warn "container $ctr is not running; cannot report its image"
    continue
  fi
  echo "  $ctr: $running"
  if docker image inspect "ghcr.io/art-of-v/$ctr:main" >/dev/null 2>&1; then
    if [[ "$(docker image inspect "ghcr.io/art-of-v/$ctr:main" --format '{{.Id}}')" \
          == "$(docker inspect "$ctr" --format '{{.Image}}')" ]]; then
      echo "    :main tag present and identical to what is running"
    else
      warn "$ctr has a local ':main' tag that is NOT what is running; a tagless 'up -d' would redeploy it"
      stale=1
    fi
  fi
done
if [[ $stale -eq 1 ]]; then
  echo "  (restart.sh pins the running tags, so a tagless up -d is not a risk once it is used)"
fi

# Step 5, on the normal path. This call was deleted along with the duplicate block in #1143 and
# nothing caught it, because the only other call sits inside --data-only — so every deploy skipped
# the data preconditions entirely and the log simply stopped after step 4. Restored here rather than
# by un-duplicating the old inline copy: the function is the version with the safety in it.
check_data_preconditions

