#!/usr/bin/env bash
# Exercises deploy/enable-pitr.sh for real, against a throwaway database.
#
# WHY THIS EXISTS
# `enable-pitr.sh` was written, syntax-checked, merged, and shipped - and had never been
# run. When it was finally run on the production server it died on its first line of
# work, because a newline had been lost between an `echo` and the comment below it:
#
#     deploy/enable-pitr.sh: line 89: echo#: command not found
#
# `bash -n` does not catch that. The line is syntactically valid - it is a command named
# `echo#` with an argument - so every syntax check passes and the script only fails when
# a person runs it, on a production box, at the moment somebody is relying on it.
#
# So this test RUNS the script. The assertion that matters is the last one: a WAL segment
# has to appear in the archive directory. Everything before it - the container starting,
# the directory being created, the ownership being right - is a step on the way to that,
# and a script that reaches the end without archiving anything has failed, whatever it
# printed.
#
# The test needs Docker. It creates its own container, its own archive directory and its
# own compose file, and it touches nothing outside its own temporary directory.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/enable-pitr-test.XXXXXX")"
TEST_CONTAINER="ff-pitr-test-$$"
TEST_ARCHIVE="$TMP/archive"
TEST_COMPOSE="$TMP/docker-compose.yml"
TEST_ENV="$TMP/.env"

pass=0
fail=0
check() {
  local what="$1" expected="$2" actual="$3"
  if [[ "$expected" == "$actual" ]]; then
    echo "  PASS  $what"; pass=$((pass + 1))
  else
    echo "  FAIL  $what (expected '$expected', got '$actual')"; fail=$((fail + 1))
  fi
}

cleanup() {
  docker rm -f "$TEST_CONTAINER" >/dev/null 2>&1 || true
  rm -rf "$TMP"
}
trap cleanup EXIT

if ! command -v docker >/dev/null 2>&1; then
  echo "  SKIP  docker is not available"
  exit 0
fi

mkdir -p "$TEST_ARCHIVE"

# Does this host do POSIX ownership at all? A Windows filesystem with a bind mount
# reports the Windows user and refuses the chown, and the script under test correctly
# REFUSES to go on in that case - so the test cannot run there, and says so rather than
# reporting a failure that is really a platform limitation. CI and the production server
# are both Linux, and that is where this is meant to run.
PG_UID="$(docker run --rm --entrypoint id postgres:16-alpine -u postgres | tr -d ' ')"
chown "$PG_UID" "$TEST_ARCHIVE" 2>/dev/null || true
if [[ "$(stat -c '%u' "$TEST_ARCHIVE")" != "$PG_UID" ]]; then
  echo "  SKIP  this host does not do POSIX ownership - the script under test refuses to"
  echo "        run without it, and that refusal is the behaviour being tested."
  echo "  0 passed, 0 failed (skipped)"
  exit 0
fi

# A compose file with the shape the real one has: the same image, the same archiving
# settings, and the archive bind-mounted in. The container name is the one the script
# will be told about.
cat > "$TEST_COMPOSE" <<COMPOSE
name: fuelflow-pitr-test
services:
  postgres:
    image: postgres:16-alpine
    container_name: ${TEST_CONTAINER}
    environment:
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: fuelflow
      POSTGRES_USER: fuelflow
    command:
      - postgres
      - -c
      - wal_level=replica
      - -c
      - archive_mode=on
      - -c
      - archive_timeout=5
      - -c
      - archive_command=test ! -f /var/lib/postgresql/wal/%f && cp %p /var/lib/postgresql/wal/%f
    volumes:
      - ${TEST_ARCHIVE}:/var/lib/postgresql/wal
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U fuelflow -d fuelflow"]
      interval: 2s
      timeout: 2s
      retries: 15
COMPOSE

printf 'POSTGRES_USER=fuelflow\nPOSTGRES_DB=fuelflow\n' > "$TEST_ENV"

# The archive directory already exists, and it exists with the WRONG owner - which is
# what a directory left behind by a deploy looks like, and the case the script has to
# deal with. A root-owned directory is not writable by the postgres user inside the
# container, so nothing would be archived and nothing would say so.
#
# There is a root-owned FILE in it as well, because a directory that is the right shape
# but holds the wrong owner's files is the quieter version of the same failure: the
# directory looks fixed, the old segments are still unwritable, and the archive stays
# empty.
chown 0:0 "$TEST_ARCHIVE"
chmod 755 "$TEST_ARCHIVE"
: > "$TEST_ARCHIVE/leftover-from-a-previous-deploy"
chown 0:0 "$TEST_ARCHIVE/leftover-from-a-previous-deploy"

out="$(CONTAINER="$TEST_CONTAINER" \
       WAL_ARCHIVE_DIR="$TEST_ARCHIVE" \
       COMPOSE_FILE="$TEST_COMPOSE" \
       ENV_FILE="$TEST_ENV" \
       PG_IMAGE=postgres:16-alpine \
       bash "$SCRIPT_DIR/enable-pitr.sh" 2>&1)"
rc=$?

echo "  --- the script's output ---"
sed 's/^/  | /' <<<"$out"
echo "  ---------------------------"

check "the script exits 0" "0" "$rc"

# The archive directory has to belong to the postgres user, or nothing can be written to
# it from inside the container.
check "the archive directory belongs to the postgres user" "$PG_UID" \
  "$(stat -c '%u' "$TEST_ARCHIVE")"
check "the files in it belong to the postgres user too" "$PG_UID" \
  "$(stat -c '%u' "$TEST_ARCHIVE/leftover-from-a-previous-deploy")"

# THE assertion. A segment in the archive is the only thing that proves archiving works;
# everything else in the script's output is a claim.
SEGMENTS="$(find "$TEST_ARCHIVE" -maxdepth 1 -name '0000000*' -type f | wc -l | tr -d ' ')"
if [[ "${SEGMENTS:-0}" -ge 1 ]]; then
  echo "  PASS  a WAL segment was archived ($SEGMENTS)"; pass=$((pass + 1))
else
  echo "  FAIL  nothing was archived - the script said it worked, and did not"; fail=$((fail + 1))
fi

# And the server's own view has to agree, one way or the other. The counter LAGS the
# files on disk, so it is only checked for the failure it reports: a segment that is
# there, with a non-zero failed_count, means the archiver is still failing.
FAILED="$(docker exec "$TEST_CONTAINER" psql -U fuelflow -d fuelflow -tAX -c \
  "SELECT failed_count FROM pg_stat_archiver" 2>/dev/null || echo "?")"
check "the archiver did not fail" "0" "${FAILED:-?}"

echo
echo "  $pass passed, $fail failed"
[[ "$fail" -eq 0 ]]
