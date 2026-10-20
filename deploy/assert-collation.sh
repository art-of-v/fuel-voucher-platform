#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Asserts that a PostgreSQL cluster sorts Cyrillic by the Ukrainian alphabet.
#
#     assert-collation.sh --psql "postgres://user:pass@host:5432/db"
#     assert-collation.sh --container fuelflow-postgres --user fuelflow --db fuelflow
#
# Exit 0 = the cluster sorts Ukrainian. Exit 1 = it does not, and says what it does
# sort by instead. Exit 2 = the check itself could not run.
#
# WHY THIS EXISTS
#
# On 2026-10-19 the production cluster was rebuilt with ICU's uk-UA collation, and
# on 2026-10-20 a review of the live database reported that it was "still on the C
# collation". The review was wrong, and it was wrong in a way that is easy to repeat:
#
#   - pg_database has THREE columns for the collation: datcollate, datctype and
#     datlocprovider, plus daticulocale in PostgreSQL 15 and later.
#   - datcollate and datctype are the LIBC names. Under the ICU provider they are
#     a vestigial string - the cluster said "C" while it was in fact sorting the
#     Ukrainian alphabet.
#   - daticulocale is the one that says which collation is in force, and
#     datlocprovider says which provider is in force ('i' for ICU, 'c' for libc).
#
# So a reader who asks "what is the collation?" and reaches for datcollate gets a
# confident wrong answer, and a document that records it becomes a false claim that
# the next reader believes.
#
# This script therefore does NOT trust a column on its own:
#
#   1. it reads daticulocale and datlocprovider, the two columns that mean it;
#   2. and then it CHECKS THE BEHAVIOUR, by sorting four Ukrainian words and
#      looking at the order.
#
# The behavioural check is the point. It cannot be wrong, it does not care what any
# column claims, and it is the thing a reader should run before writing a sentence
# about the collation into a document.
#
# The same script is used by the CI migration dry run, so the container that applies
# a pending migration is PROVEN to sort the way production sorts, rather than
# assumed to.
# ------------------------------------------------------------------------------
set -uo pipefail

EXPECTED_LOCALE="${EXPECTED_LOCALE:-uk-UA}"

# The words, and the order a Ukrainian reader expects. 'Ї' is the tell: in the
# Ukrainian alphabet it comes after 'Г' and before 'Я', and by code point it comes
# FIRST - U+0407 is below U+0411 ('Б'). A cluster that sorts by code point puts
# 'Їжак' at the head of this list.
UKRAINIAN_PROBE="'Їжак','Ганок','Яблуко','Бджола'"
UKRAINIAN_EXPECTED="Бджола,Ганок,Їжак,Яблуко"

fail() { echo "ERROR: $*" >&2; exit 2; }

# --- how to reach the cluster ---------------------------------------------------
PSQL_URI=""
CONTAINER=""
PGUSER_ARG=""
PGDB_ARG=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --psql)      PSQL_URI="${2:-}"; shift 2 ;;
    --container) CONTAINER="${2:-}"; shift 2 ;;
    --user)      PGUSER_ARG="${2:-}"; shift 2 ;;
    --db)        PGDB_ARG="${2:-}"; shift 2 ;;
    --expected)  EXPECTED_LOCALE="${2:-}"; shift 2 ;;
    -h|--help)   sed -n '2,50p' "$0"; exit 0 ;;
    *)           fail "unknown argument: $1" ;;
  esac
done

[[ -n "$PSQL_URI" || -n "$CONTAINER" ]] || fail "pass --psql <uri> or --container <name>"

# One function, two ways in, so the rest of the script does not care which.
if [[ -n "$PSQL_URI" ]]; then
  run_sql() { psql "$PSQL_URI" -X -tAX -c "$1" 2>/dev/null; }
  db_label() { echo "$PSQL_URI" | sed -E 's#//[^@]*@#//***@#'; }
else
  run_sql() { docker exec "$CONTAINER" psql -U "$PGUSER_ARG" -d "$PGDB_ARG" -X -tAX -c "$1" 2>/dev/null; }
  db_label() { echo "$CONTAINER/$PGDB_ARG"; }
fi

# --- 1. what the cluster claims --------------------------------------------------
LOCALE_ROW="$(run_sql "SELECT COALESCE(daticulocale::text,'(none)') || '|' || datlocprovider::text || '|' || datcollate::text FROM pg_database WHERE datname = current_database();")"
[[ -n "$LOCALE_ROW" ]] || fail "could not read the collation of $(db_label)"

IFS='|' read -r DATICULOCALE PROVIDER COLLATE <<< "$LOCALE_ROW"
echo "cluster:      $(db_label)"
echo "daticulocale: ${DATICULOCALE}"
echo "provider:     ${PROVIDER}   (i = ICU, c = libc)"
echo "datcollate:   ${COLLATE}   (the libc name - under ICU it says nothing about the sort order)"

# --- 2. what the cluster DOES ----------------------------------------------------
# string_agg over a VALUES list, ordered by the column, is the sort. The words are
# written as literals rather than passed as parameters so this runs on any client.
ACTUAL="$(run_sql "SELECT string_agg(t.v, ',' ORDER BY t.v) FROM (VALUES ('Їжак'),('Ганок'),('Яблуко'),('Бджола')) AS t(v);")"
[[ -n "$ACTUAL" ]] || fail "could not run the sort probe against $(db_label)"

echo "sorts as:     ${ACTUAL}"

if [[ "$ACTUAL" != "$UKRAINIAN_EXPECTED" ]]; then
  echo >&2
  echo "ERROR: $(db_label) does not sort by the Ukrainian alphabet." >&2
  echo "       expected: ${UKRAINIAN_EXPECTED}" >&2
  echo "       got:      ${ACTUAL}" >&2
  echo >&2
  if [[ "$PROVIDER" == "c" ]]; then
    echo "       The provider is libc, so the cluster is comparing by code point." >&2
    echo "       PostgreSQL 16 cannot ALTER DATABASE ... LC_COLLATE - the cluster has to be" >&2
    echo "       rebuilt: deploy/rebuild-collation.sh (or --staging for the staging box)." >&2
  else
    echo "       The provider is ICU but the locale is '${DATICULOCALE}', not '${EXPECTED_LOCALE}'." >&2
  fi
  echo >&2
  echo "       A NEW volume is created with the right locale from POSTGRES_INITDB_ARGS; an" >&2
  echo "       EXISTING one is changed by the rebuild script above." >&2
  exit 1
fi

echo "OK: sorts by the Ukrainian alphabet."
exit 0
