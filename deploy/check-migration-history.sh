#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Refuses a repository that has lost a migration production has already applied.
#
#     deploy/check-migration-history.sh <history-file> <assembly-file> [compensated-file]
#
# WHY THIS EXISTS
#
# The migration history is append-only: a migration that any deployed database has applied stays in
# the folder, in order, under its original id. That is the rule, and nothing enforced it.
#
# On 2026-09-11 a commit deleted eight migration files with the reasoning that "EF scans the
# assembly, already-applied history rows are unaffected" - which is true for the databases that
# already have them and irrelevant for every database that does not.
#
# It turned out to be harmless, but by luck rather than by design: a later migration,
# 20260919113317_AddMissingDriftedTables, re-creates everything those eight created, with
# IF NOT EXISTS. A from-zero build and a restored copy of production come out the same shape. The
# next deletion might not be so lucky, and nothing in the build would have said so.
#
# WHAT DOES NOW SAY SO: the schema comparison (deploy/diff-schema.sh, wired into ci.yml as the
# fromzero-schema job) builds the schema from zero and compares it with production's shape.
# This script checks the IDS; that one checks what the ids produce. Both are needed - an id can
# be present and produce something different, and that is exactly what happened with the check
# constraints: the compensating migration creates them with raw SQL and EF's own migrations
# create them through the model, so the same rule is stored in two different forms, and only
# the schema comparison could see it.
#
# WHAT IT COMPARES
#
# Two lists of migration ids, one per line:
#
#   <history-file>    the ids in a database's __EFMigrationsHistory
#   <assembly-file>   the ids EF can discover in this repository
#
# Every id in the history must be in the assembly, or in the compensated list. An id that is in
# neither is a migration production has applied and this repository no longer contains - which
# means a fresh database would come out with a different schema, and the dry run would have
# "passed" while proving the opposite.
#
# The other direction is NOT an error. The assembly may hold migrations production has not applied
# yet - that is what a pending migration is - so an assembly id with no history row is expected.
#
# THE COMPENSATED LIST
#
# A third file, optional, naming the ids that are missing because something else creates what
# they created. See deploy/compensated-migration-ids.txt for the eight and the reason.
#
# EXIT
#   0  every applied migration is still in the repository, or is compensated for
#   1  at least one is missing; the missing ids are printed
#   2  the files could not be read
# ------------------------------------------------------------------------------
set -euo pipefail

HISTORY="${1:-}"
ASSEMBLY="${2:-}"
COMPENSATED="${3:-}"

if [[ -z "$HISTORY" || -z "$ASSEMBLY" ]]; then
  echo "usage: $0 <history-file> <assembly-file> [compensated-file]" >&2
  exit 2
fi
if [[ ! -r "$HISTORY" ]]; then
  echo "ERROR: cannot read the history file: $HISTORY" >&2
  exit 2
fi
if [[ ! -r "$ASSEMBLY" ]]; then
  echo "ERROR: cannot read the assembly file: $ASSEMBLY" >&2
  exit 2
fi
if [[ -n "$COMPENSATED" && ! -r "$COMPENSATED" ]]; then
  echo "ERROR: cannot read the compensated file: $COMPENSATED" >&2
  exit 2
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Both sides are sorted and de-duplicated: the order is irrelevant to the question, and the same
# id must not be reported twice because the history table happened to carry it twice.
#
# The carriage returns are stripped because these files arrive from three places - psql, a git
# checkout, a shell redirect - and two of them use CRLF. A trailing \r makes an id fail a
# whole-file comparison, which would report every id as missing.
grep -E '^[0-9]{14}_' "$HISTORY"  | tr -d '\r' | sort -u > "$WORK/history.ids"
grep -E '^[0-9]{14}_' "$ASSEMBLY" | tr -d '\r' | sort -u > "$WORK/assembly.ids"

# The compensated list carries comments, so it is filtered the same way.
: > "$WORK/compensated.ids"
if [[ -n "$COMPENSATED" ]]; then
  grep -E '^[0-9]{14}_' "$COMPENSATED" | tr -d '\r' | sort -u > "$WORK/compensated.ids"
fi

# comm needs both sides sorted, and it reports the lines unique to the FIRST file.
missing="$(comm -23 "$WORK/history.ids" "$WORK/assembly.ids")"

# A migration id that is missing but compensated for is fine: something else creates what it
# created. Only the ones that are missing AND uncompensated are a failure.
uncompensated=""
while IFS= read -r id; do
  [[ -z "$id" ]] && continue
  if ! grep -qxF "$id" "$WORK/compensated.ids"; then
    uncompensated+="$id"$'\n'
  fi
done <<< "$missing"

if [[ -n "$uncompensated" ]]; then
  {
    echo "ERROR: production has applied migrations that this repository does not contain"
    echo "and nothing else creates what they created."
    echo "Each of these is in a deployed database's __EFMigrationsHistory and is not in the"
    echo "migration folder. A fresh database would migrate without it and end up with a"
    echo "different schema than production. Restore the files."
    echo
    printf '%s' "$uncompensated"
  } >&2
  exit 1
fi

compensated_count="$(wc -l < "$WORK/compensated.ids" | tr -d ' ')"
echo "every applied migration is still in the repository ($(wc -l < "$WORK/history.ids") ids checked, $compensated_count compensated for)"
