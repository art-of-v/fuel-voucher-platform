#!/usr/bin/env bash
# Shared helpers for the point-in-time recovery scripts. Sourced, not executed.
#
# The one thing these scripts have in common is that they have to tell the truth about
# whether WAL archiving is working, because everything else - the base backup, the
# recovery point objective, the whole point of the exercise - depends on it.
#
# There is a counter in PostgreSQL for this: pg_stat_archiver.archived_count. It is
# tempting to gate on it, and it is the wrong thing to gate on, because it LAGS.
#
# Measured while writing this: a WAL segment was copied into the archive at 19:04:19 and
# the counter still read 0 for another 27 seconds, until the NEXT segment was archived.
# A check that reads the counter can therefore report "archiving is broken" while the
# archive is full of fresh segments - and, on a quiet database, that is most of the time.
#
# So the check looks at the ARCHIVE DIRECTORY, which cannot lag: if the newest segment
# on disk is recent, then archiving is working, whatever the counter says. The counter is
# still read, because it is the thing that says whether the archiver is ERRORING - which
# is the other half of the question.

# The newest segment in the archive, as an epoch timestamp. Prints nothing if there is
# none.
newest_archived_segment() {
  local dir="$1"
  [[ -d "$dir" ]] || return 1
  find "$dir" -maxdepth 1 -name '0000000*' -type f -printf '%T@\n' 2>/dev/null \
    | sort -n | tail -1 | cut -d. -f1
}

# check_wal_archive <container> <archive-dir> <user> <database> [max-age-seconds]
#
# Prints what it found. Returns 0 when archiving is healthy, 1 when it is not.
check_wal_archive() {
  local container="$1" dir="$2" user="$3" database="$4" max_age="${5:-1800}"
  local newest newest_age now row archived failed last_failed
  local failures=0

  now="$(date +%s)"

  # --- the archive directory, which cannot lie -----------------------------------------
  if [[ ! -d "$dir" ]]; then
    echo "ERROR: the archive directory does not exist: $dir" >&2
    return 1
  fi
  newest="$(newest_archived_segment "$dir" || true)"
  if [[ -z "$newest" ]]; then
    echo "ERROR: the archive directory $dir contains no WAL segments at all." >&2
    echo "       Nothing has ever been archived, so point-in-time recovery is not" >&2
    echo "       available. The usual cause is the directory not being writable by the" >&2
    echo "       postgres user - see deploy/enable-pitr.sh." >&2
    return 1
  fi
  newest_age=$(( now - newest ))
  if (( newest_age > max_age )); then
    echo "ERROR: the newest WAL segment in $dir is ${newest_age}s old (limit ${max_age}s)." >&2
    echo "       Archiving has stopped, so the archive has a hole in it." >&2
    echo "       The database itself is fine - this is the archive command failing, not" >&2
    echo "       the server. The server's log says why:" >&2
    docker logs "$container" 2>&1 | grep -i 'archive command failed' | tail -3 >&2
    return 1
  fi
  echo "[$(date -Is)] WAL archive on disk: the newest segment is ${newest_age}s old."

  # --- what the server thinks ----------------------------------------------------------
  row="$(docker exec "$container" psql -U "$user" -d "$database" -tAX -F '|' -c \
    "SELECT archived_count, failed_count, COALESCE(EXTRACT(epoch FROM (now() - last_failed_time))::bigint, -1) FROM pg_stat_archiver" 2>/dev/null || true)"
  if [[ -z "$row" ]]; then
    echo "WARNING: could not read pg_stat_archiver - the archive looks right on disk, but" >&2
    echo "         the server's own view of it is unavailable." >&2
    return 1
  fi
  IFS='|' read -r archived failed last_failed <<<"$row"
  echo "[$(date -Is)] Server's view: ${archived:-?} segments archived, ${failed:-?} failed."

  # A failure that is OLDER than the newest segment is a failure that has been fixed -
  # the archiver moved on. A failure that is NEWER than the newest segment means the
  # archiver is still failing.
  if [[ "${failed:-0}" -gt 0 && "${last_failed:--1}" -ge 0 && "${last_failed:--1}" -lt "${newest_age}" ]]; then
    echo "ERROR: the archiver's most recent failure is more recent than the newest" >&2
    echo "       archived segment, so archiving is currently broken." >&2
    docker logs "$container" 2>&1 | grep -i 'archive command failed' | tail -3 >&2
    failures=1
  fi
  return $failures
}
