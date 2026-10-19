#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Refuses a repository that has lost a migration production has already applied.
#
#     deploy/check-migration-history.sh <history-file> <assembly-file>
#
# WHY THIS EXISTS
#
# The migration history is append-only: a migration that any deployed database has applied stays in
# the folder, in order, under its original id. That is the rule, and nothing enforced it.
#
# On 2026-09-11 a commit deleted eight migration files with the reasoning that "EF scans the
# assembly, already-applied history rows are unaffected" - which is true for the databases that
# already have them and irrelevant for every database that does not. A fresh environment would
# have migrated without them and ended up with a different schema than production.
#
# It turned out to be harmless, but by luck rather than by design: a later migration,
# 20260919113317_AddMissingDriftedTables, re-creates everything those eight created, with
# IF NOT EXISTS. A from-zero build and a restored copy of production come out the same shape. The
# next deletion might not be so lucky, and nothing in the build would have said so.
#
# WHAT IT COMPARES
#
# Two lists of migration ids, one per line:
#
#   <history-file>    the ids in a database's __EFMigrationsHistory
#   <assembly-file>   the ids EF can discover in this repository
#
# Every id in the history must be in the assembly. An id that is not is a migration production
# has applied and this repository no longer contains - which means a fresh database would come out
# with a different schema, and the dry run would have "passed" while proving the opposite.
#
# The other direction is NOT an error. The assembly may hold migrations production has not applied
# yet - that is what a pending migration is - so an assembly id with no history row is expected.
#
# EXIT
#   0  every applied migration is still in the repository
#   1  at least one is missing; the missing ids are printed
#   2  the two files could not be read
# ------------------------------------------------------------------------------
set -euo pipefail

HISTORY="${1:-}"
ASSEMBLY="${2:-}"

if [[ -z "$HISTORY" || -z "$ASSEMBLY" ]]; then
  echo "usage: $0 <history-file> <assembly-file>" >&2
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

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Both sides are sorted and de-duplicated: the order is irrelevant to the question, and the same
# id must not be reported twice because the history table happened to carry it twice.
grep -E '^[0-9]{14}_' "$HISTORY"  | sort -u > "$WORK/history.ids"
grep -E '^[0-9]{14}_' "$ASSEMBLY" | sort -u > "$WORK/assembly.ids"

# comm needs both sides sorted, and it reports the lines unique to the FIRST file.
missing="$(comm -23 "$WORK/history.ids" "$WORK/assembly.ids")"

if [[ -n "$missing" ]]; then
  {
    echo "ERROR: production has applied migrations that this repository does not contain."
    echo "Each of these is in a deployed database's __EFMigrationsHistory and is not in the"
    echo "migration folder. A fresh database would migrate without it and end up with a"
    echo "different schema than production. Restore the files."
    echo
    echo "$missing"
  } >&2
  exit 1
fi

echo "every applied migration is still in the repository ($(wc -l < "$WORK/history.ids") ids checked)"
