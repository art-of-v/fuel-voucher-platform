import re

TUNING_LINES = [
    "# Postgres tuning. The same reasoning as production, and the same values, because a",
    "# setting that only exists on one box is one nobody remembers exists - and the first",
    "# time it matters is the day somebody needs to use it. See docker-compose.prod.yml for",
    "# the reasoning behind each value; it is the same list.",
    "#",
    "# THE PLANNER. random_page_cost 4 and effective_io_concurrency 1 are PostgreSQL's",
    "# defaults, and those defaults are for a magnetic spindle. The disk is an SSD, where a",
    "# random read costs about what a sequential one does, so the planner was told index",
    "# scans are four times more expensive than they are.",
    "#",
    "# THE TIMEOUTS. All three were unbounded. The one that matters is",
    "# idle_in_transaction_session_timeout: a connection that opens a transaction and walks",
    "# away holds its snapshot, and that is what pins the vacuum horizon and stops VACUUM from",
    "# reclaiming dead rows anywhere in the database.",
    "#",
    "# THE LOGS. Three settings that cost nothing and say what used to be invisible: a query",
    "# that spills to disk, a lock wait, and an autovacuum that ran long enough to change the",
    "# plan underneath a query.",
    "#",
    "# WHY THEY ARE HERE AND NOT ONLY ON THE BOX. They were applied to the running server",
    "# with ALTER SYSTEM, which writes postgresql.auto.conf - and that file lives in the",
    "# container's volume. A box rebuilt from this file, or a disaster recovery on a new",
    "# server, would come up with PostgreSQL's defaults and every one of these would silently",
    "# be back to what it was.",
]

CMD_LINES = [
    "- postgres",
    "- -c",
    "- effective_cache_size=512MB",
    "- -c",
    "- shared_preload_libraries=pg_stat_statements",
    "- -c",
    "- log_min_duration_statement=1000",
    "- -c",
    "- track_io_timing=on",
]

path = 'deploy/docker-compose.staging.yml'
src = open(path, encoding='utf-8', newline='').read()
eol = '\r\n' if '\r\n' in src else '\n'

# 1. the command block: the five lines, then whatever follows (the WAL archiving comment)
pat = re.compile(
    r'(?P<ind>[ ]*)command:\r?\n'
    r'(?P=ind)  - postgres\r?\n'
    r'(?P=ind)  - -c\r?\n'
    r'(?P=ind)  - effective_cache_size=512MB\r?\n'
    r'(?P=ind)  - -c\r?\n'
    r'(?P=ind)  - shared_preload_libraries=pg_stat_statements\r?\n'
    r'(?P=ind)  - -c\r?\n'
    r'(?P=ind)  - log_min_duration_statement=1000\r?\n'
    r'(?P=ind)  - -c\r?\n'
    r'(?P=ind)  - track_io_timing=on\r?\n')
m = pat.search(src)
assert m, path
ind = m.group('ind')

extra = [
    "- -c",
    "- random_page_cost=1.1",
    "- -c",
    "- effective_io_concurrency=200",
    "- -c",
    "- statement_timeout=30s",
    "- -c",
    "- lock_timeout=10s",
    "- -c",
    "- idle_in_transaction_session_timeout=60s",
    "- -c",
    "- log_temp_files=10MB",
    "- -c",
    "- log_lock_waits=on",
    "- -c",
    "- log_autovacuum_min_duration=1000",
    "- -c",
    "- shared_buffers=256MB",
    "- -c",
    "- max_connections=50",
]

block = f'{ind}command:' + eol + eol.join(f'{ind}  {l}' for l in CMD_LINES) + eol
src = src[:m.start()] + block + src[m.end():]

# the extra settings go AFTER the WAL archiving block, so the archiving comment keeps its
# place next to the archiving lines. Find the end of the command list: the last "-c" line
# before a line that is not part of it. Simpler: append the extra settings right after the
# archiving options, which end with the last "-c <something>" line of the block.
lines = src.split(eol)
start = next(i for i, l in enumerate(lines) if l.strip() == 'command:')
# the end of the command list is the first line after it that is not a "- ..." item
end = start + 1
while end < len(lines) and lines[end].strip().startswith('- '):
    end += 1
insert_at = end
for k, l in enumerate(extra):
    lines.insert(insert_at + k, f'{ind}  {l}')
src = eol.join(lines)

# 2. the tuning comment: put it above the command block
old_c = f'{ind}# Postgres tuning and query visibility. Measured on this box 2026-10-19 and left as it was found:'
if src.count(old_c) == 1:
    src = src.replace(old_c, eol.join(f'{ind}{l}' for l in TUNING_LINES), 1)
else:
    # the staging file has no such comment yet; put ours directly above `command:`
    src = src.replace(f'{ind}command:', eol.join(f'{ind}{l}' for l in TUNING_LINES) + eol + f'{ind}command:', 1)

# 3. the initdb args: add --data-checksums
old_i = 'POSTGRES_INITDB_ARGS: "--locale-provider=icu --icu-locale=uk-UA --locale=C.UTF-8"'
assert src.count(old_i) == 1, src.count(old_i)
src = src.replace(
    old_i,
    'POSTGRES_INITDB_ARGS: "--locale-provider=icu --icu-locale=uk-UA --locale=C.UTF-8 --data-checksums"', 1)

open(path, 'w', encoding='utf-8', newline='').write(src)
print('updated', path)
