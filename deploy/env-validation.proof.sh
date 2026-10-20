#!/usr/bin/env bash
# Proof that env-validation.test.sh catches the two real defects from #219.
# Kept out of CI on purpose: it asserts on deliberately broken files.
set -uo pipefail
cd "$(dirname "$0")"

fail=0

check() {
  local label="$1" file="$2"
  local out
  out="$(bash -c ". ./env-validation.sh-helper 2>/dev/null; true" 2>/dev/null || true)"
  # Run the real validator against the broken file by temporarily swapping it in.
  cp "$file" .env.staging.example.probe
  out="$(sed 's#\.env\.staging\.example#.env.staging.example.probe#g' env-validation.test.sh | bash -s 2>&1)"
  rm -f .env.staging.example.probe

  if echo "$out" | grep -q 'FAIL'; then
    echo "  ok    catches $label"
    echo "$out" | grep 'FAIL' | sed 's/^/          /'
  else
    echo "  FAIL  does NOT catch $label"
    fail=1
  fi
}

# 1. A line beginning with a backslash before the hash.
{ printf '\\# looks like a comment but is an assignment\n'
  printf 'STAGE_MARKER=(unclosed paren on the next line\n'
  cat .env.staging.example
} > .broken-escaped-comment

# 2. An unquoted value containing whitespace and a > - the MONOBANK_PUBLIC_KEY case.
{ cat .env.staging.example
  printf 'MONOBANK_PUBLIC_KEY=-----BEGIN PUBLIC KEY----- abc >def\n'
} > .broken-unquoted

check "a \\# line" .broken-escaped-comment
check "an unquoted whitespace value" .broken-unquoted

rm -f .broken-escaped-comment .broken-unquoted
echo
[ "$fail" -eq 0 ] && echo "both real defects are detected" || echo "AT LEAST ONE DEFECT SLIPS THROUGH"
exit "$fail"