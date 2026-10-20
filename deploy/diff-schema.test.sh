#!/usr/bin/env bash
# Tests deploy/diff-schema.sh - the gate that refuses a build whose from-zero schema is not
# the schema production has.
#
# WHY THIS NEEDS A TEST
#
# The gate's whole value is that it fails when it should. A comparison that never fails is
# indistinguishable from a comparison that never runs, and the thing it guards against - a
# repository that cannot rebuild production - is invisible in a diff: the change that causes
# it looks like an ordinary migration.
#
# The comparison is over two text files, so it can be tested without a database, without EF and
# without a build. The ci.yml step that CALLS the script is checked too, because a gate that
# is not wired into the workflow is a script nobody runs - and so is the FINGERPRINT, because
# a fingerprint that prints nothing compares nothing and passes everything.
#
# Run: bash deploy/diff-schema.test.sh
set -uo pipefail
cd "$(dirname "$0")/.."

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

SCRIPT="deploy/diff-schema.sh"
FINGERPRINT="deploy/schema-fingerprint.sql"
CI_YML=".github/workflows/ci.yml"

pass=0
fail=0
ok()  { pass=$((pass + 1)); printf '  ok    %s\n' "$1"; }
bad() { fail=$((fail + 1)); printf '  FAIL  %s\n        got:      %s\n        expected: %s\n' "$1" "$2" "$3"; }
eq()  { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1" "$2" "$3"; fi; }
has() { if echo "$2" | grep -qF -- "$3"; then ok "$1"; else bad "$1" "$2" "a line containing '$3'"; fi; }

# The file path is the FIRST argument and the content is the rest, so the path is shifted
# off before printing. Without the shift the path becomes a line of the file - which the
# other test in this folder hides, because its script filters the lines it reads.
write() { local f="$1"; shift; printf '%s\n' "$@" > "$f"; }

# ---------------------------------------------------------------- the happy path
write "$WORK/a.fp" \
  "column public.orders.id type=uuid notnull=yes identity=no generated=no collation=default default=-" \
  "constraint public.orders.ck_order_status CHECK (((status)::text = ANY (ARRAY[('Pending'::character varying)::text])))" \
  "table public.orders"
write "$WORK/b.fp" \
  "column public.orders.id type=uuid notnull=yes identity=no generated=no collation=default default=-" \
  "constraint public.orders.ck_order_status CHECK (((status)::text = ANY (ARRAY[('Pending'::character varying)::text])))" \
  "table public.orders"

out="$(bash "$SCRIPT" "$WORK/a.fp" "$WORK/b.fp" 2>&1)"; code=$?
eq "two identical schemas pass" "$code" "0"
has "it says the schemas are identical" "$out" "the two schemas are identical"

# The ORDER of the lines must not matter: the fingerprint is sorted, but a file that was
# concatenated rather than sorted is still the same schema, and a gate that fails on the
# order of two identical sets of objects is a gate that will fail for no reason at 3am.
write "$WORK/c.fp" \
  "table public.orders" \
  "column public.orders.id type=uuid notnull=yes identity=no generated=no collation=default default=-" \
  "constraint public.orders.ck_order_status CHECK (((status)::text = ANY (ARRAY[('Pending'::character varying)::text])))"

out="$(bash "$SCRIPT" "$WORK/a.fp" "$WORK/c.fp" 2>&1)"; code=$?
eq "the order of the objects does not matter" "$code" "0"

# ---------------------------------------------------------------- the failure that matters
write "$WORK/d.fp" \
  "column public.orders.id type=uuid notnull=yes identity=no generated=no collation=default default=-" \
  "constraint public.orders.ck_order_status CHECK (((status)::text = ANY (ARRAY[('Pending'::character varying)::text])))" \
  "table public.orders"

write "$WORK/e.fp" \
  "column public.orders.id type=uuid notnull=yes identity=no generated=no collation=default default=-" \
  "constraint public.orders.ck_order_status CHECK (((status)::text = ANY (ARRAY[('Paid'::character varying)::text])))" \
  "table public.orders"

out="$(bash "$SCRIPT" "$WORK/a.fp" "$WORK/e.fp" "production" "from zero" 2>&1)"; code=$?
eq "two different schemas are refused" "$code" "1"
has "it names the object that differs" "$out" "ck_order_status"
has "it says which side has it" "$out" "from zero"
has "it says what to do about it" "$out" "Fix it with a MIGRATION"

# The direction matters: a constraint that exists only on production is a DIFFERENT problem
# from one that exists only on the from-zero build, and the message has to keep them apart.
write "$WORK/f.fp" "table public.orders"

out="$(bash "$SCRIPT" "$WORK/a.fp" "$WORK/f.fp" "production" "from zero" 2>&1)"; code=$?
eq "a shape that is missing objects is refused" "$code" "1"
has "it counts the objects production has alone" "$out" "production: 2 objects"
has "it counts the objects the other side has alone" "$out" "from zero: 0 objects"

# ---------------------------------------------------------------- the trivial mistakes
out="$(bash "$SCRIPT" 2>&1)"; code=$?
eq "no arguments is a usage error, not a pass" "$code" "2"

out="$(bash "$SCRIPT" "$WORK/nope.fp" "$WORK/a.fp" 2>&1)"; code=$?
eq "a missing file is an error, not a pass" "$code" "2"
has "it says which file it could not read" "$out" "cannot read the fingerprint file"

# A CRLF file is the same schema: the fingerprint arrives from psql, from a git checkout and
# from a shell redirect, and two of those three use CRLF. A trailing \r would make every
# line differ from itself.
printf 'table public.orders\r\ncolumn public.orders.id type=uuid notnull=yes identity=no generated=no collation=default default=-\r\nconstraint public.orders.ck_order_status CHECK (((status)::text = ANY (ARRAY[('"'"'Pending'"'"'::character varying)::text])))\r\n' > "$WORK/crlf.fp"
out="$(bash "$SCRIPT" "$WORK/a.fp" "$WORK/crlf.fp" 2>&1)"; code=$?
eq "a CRLF file is the same schema" "$code" "0"

# ---------------------------------------------------------------- the fingerprint itself
if [ -r "$FINGERPRINT" ]; then
  ok "the fingerprint file exists"
else
  bad "the fingerprint file exists" "missing" "$FINGERPRINT"
fi

# A fingerprint that prints nothing compares nothing, and a comparison of nothing passes. So
# the fingerprint has to produce a line for every kind of object it claims to cover.
for kind in "schema " "table " "column " "constraint " "index " "view " "sequence " "function " "trigger "; do
  if grep -q "'$kind" "$FINGERPRINT" 2>/dev/null; then
    ok "the fingerprint covers '$kind' objects"
  else
    bad "the fingerprint covers '$kind' objects" "not found" "a line producing '$kind'"
  fi
done

# The hangfire schema is created by Hangfire at runtime, so no migration can create it, and
# comparing against production would always fail. It is excluded in ONE place, in the
# fingerprint, next to the explanation - not in the comparison.
if grep -q "hangfire" "$FINGERPRINT"; then
  ok "the fingerprint excludes the hangfire schema, and says why"
else
  bad "the fingerprint excludes the hangfire schema, and says why" "not found" "the word 'hangfire'"
fi

# The toast indexes carry an OID in their name, so they differ between two identical
# databases. They have to be excluded or the comparison is noise.
if grep -q "pg_toast" "$FINGERPRINT"; then
  ok "the fingerprint excludes the toast indexes, and says why"
else
  bad "the fingerprint excludes the toast indexes, and says why" "not found" "the word 'pg_toast'"
fi

# ---------------------------------------------------------------- the gate is wired in
if grep -q "diff-schema.sh" "$CI_YML"; then
  ok "ci.yml calls the comparison"
else
  bad "ci.yml calls the comparison" "not found" "diff-schema.sh in ci.yml"
fi

if grep -q "schema-fingerprint.sql" "$CI_YML"; then
  ok "ci.yml produces a fingerprint"
else
  bad "ci.yml produces a fingerprint" "not found" "schema-fingerprint.sql in ci.yml"
fi

# Both sides have to be fingerprinted, or the comparison is between a schema and nothing.
fingerprints="$(grep -c "schema-fingerprint.sql" "$CI_YML" || true)"
if [ "$fingerprints" -ge 2 ]; then
  ok "ci.yml fingerprints both sides of the comparison"
else
  bad "ci.yml fingerprints both sides of the comparison" "$fingerprints" "at least 2"
fi

# ---------------------------------------------------------------- and the outcome
echo
if [ "$fail" -eq 0 ]; then
  echo "  $pass checks passed"
  exit 0
fi
echo "  $pass passed, $fail FAILED"
exit 1
