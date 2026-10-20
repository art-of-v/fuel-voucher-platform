#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Compares two schema fingerprints and refuses the build when they differ.
#
#     deploy/diff-schema.sh <fingerprint-a> <fingerprint-b> [label-a] [label-b]
#
# WHY THIS EXISTS
#
# The question is "can this repository rebuild what production has?" - and until now nothing in
# the build answered it. The dry run proves that a pending migration applies to production's
# shape, which is a different question: it proves the migration is SAFE on production, not that
# the repository DESCRIBES production. A repository can pass every dry run and still be unable
# to rebuild the database it is deploying to.
#
# It was not a theoretical worry. On 2026-10-20 this comparison was run for the first time, and
# it found 48 objects that a from-zero build and production did not agree on:
#
#   * 46 check constraints, all of them stored in a different form. The repository's
#     compensating migration 20260919113317_AddMissingDriftedTables creates them with raw SQL,
#     and EF's own migrations create them through the model - and the two forms are stored
#     differently. The constraints ENFORCE the same thing; the database remembers them
#     differently. Nobody had noticed, because nothing ever compared the two.
#   * one partial index (ix_webhook_events_outstanding), for the same reason.
#   * one column default (refresh_tokens.family_id), which is a real difference: production
#     carried a default that no migration in this repository creates, because the migration
#     that created it was one of the eight deleted on 2026-09-11.
#
# The migration 20261020161912_TheSchemaIsTheModelOnEveryDatabase closed all of them. This
# script is what keeps them closed.
#
# WHAT IT COMPARES
#
# Two fingerprint files, one object per line, as produced by deploy/schema-fingerprint.sql.
# The comparison is EXACT: the files must be byte-identical. There is no list of accepted
# differences, and that is the point - a ledger of "known differences" is a list that only
# grows, and the next person to add to it adds a difference nobody has checked.
#
# The one thing that IS excluded lives in the fingerprint itself (the hangfire schema, which
# Hangfire creates at runtime and no migration can), not here. A rule in the comparison is a
# rule somebody forgets to widen; a rule in the fingerprint sits next to what it excludes.
#
# EXIT
#   0  the two schemas are identical
#   1  they differ; the differing objects are printed
#   2  a file could not be read
# ------------------------------------------------------------------------------
set -euo pipefail

A="${1:-}"
B="${2:-}"
LABEL_A="${3:-the first schema}"
LABEL_B="${4:-the second schema}"

if [[ -z "$A" || -z "$B" ]]; then
  echo "usage: $0 <fingerprint-a> <fingerprint-b> [label-a] [label-b]" >&2
  exit 2
fi
if [[ ! -r "$A" ]]; then
  echo "ERROR: cannot read the fingerprint file: $A" >&2
  exit 2
fi
if [[ ! -r "$B" ]]; then
  echo "ERROR: cannot read the fingerprint file: $B" >&2
  exit 2
fi

# The carriage returns are stripped because these files arrive from three places - psql, a git
# checkout, a shell redirect - and two of them use CRLF. A trailing \r makes a line differ from
# itself, which would report every line as different.
strip() { tr -d '\r' < "$1" | grep -v '^[[:space:]]*$'; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
strip "$A" | sort > "$WORK/a.txt"
strip "$B" | sort > "$WORK/b.txt"

only_a="$(comm -23 "$WORK/a.txt" "$WORK/b.txt" || true)"
only_b="$(comm -13 "$WORK/a.txt" "$WORK/b.txt" || true)"

count_a="$(printf '%s' "$only_a" | grep -c . || true)"
count_b="$(printf '%s' "$only_b" | grep -c . || true)"

if [[ "$count_a" -eq 0 && "$count_b" -eq 0 ]]; then
  lines="$(wc -l < "$WORK/a.txt" | tr -d ' ')"
  echo "the two schemas are identical ($lines objects)"
  exit 0
fi

{
  echo "ERROR: the two schemas are not the same shape."
  echo
  echo "  $LABEL_A: $count_a objects it has and the other does not"
  echo "  $LABEL_B: $count_b objects it has and the other does not"
  echo
  echo "A database built from this repository's migrations and a database built from"
  echo "production's shape do not agree. One of them holds something the other cannot"
  echo "rebuild - which means a fresh deployment of this repository would come up with a"
  echo "different schema than the one running."
  echo
  echo "In $LABEL_A only:"
  printf '%s\n' "$only_a" | head -60
  if [[ "$count_a" -gt 60 ]]; then echo "  ... and $((count_a - 60)) more"; fi
  echo
  echo "In $LABEL_B only:"
  printf '%s\n' "$only_b" | head -60
  if [[ "$count_b" -gt 60 ]]; then echo "  ... and $((count_b - 60)) more"; fi
  echo
  echo "Fix it with a MIGRATION, not by editing the database: a hand-made change on one"
  echo "database is exactly the difference this gate is looking for."
} >&2
exit 1
