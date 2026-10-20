import re

TUNING = """      # Postgres tuning and query visibility. Measured on this box 2026-10-19 and left as it was found:
      #
      #   effective_cache_size  was 4 GB on a 512 MB container (and a 4 GB host). The planner reads
      #                          this as "assume the whole box is cache", which is how a seq scan on a
      #                          30 MB database starts costing index scans it cannot afford. 1 GB is
      #                          what this host can actually hold for Postgres.
      #   shared_preload_libraries  empty, so there was no query-level visibility at all: Prometheus
      #                          and Grafana watched the container and nothing inside it. This is the
      #                          cheapest possible way to see which statements the storefront runs.
      #   log_min_duration_statement  -1, i.e. off. One second is pathological for this workload and
      #                          costs nothing while it stays pathological.
      #   track_io_timing      off, which makes EXPLAIN's I/O timings unavailable when they are
      #                          exactly the thing worth explaining.
      #
      # pg_stat_statements itself is created by a migration rather than an initdb script, so every
      # environment gets it and a missing one fails loudly instead of silently going without.
      #
      # THE PLANNER, 2026-10-20. The two cost constants were PostgreSQL's defaults, and those
      # defaults are for a magnetic spindle:
      #
      #   random_page_cost 4        the default. It tells the planner a random read costs four
      #                               times what a sequential one does. The disk is an SSD (lsblk
      #                               reports ROTA 0), where it costs about the same, so the
      #                               planner was told index scans are four times more expensive
      #                               than they are - and answered "sequential scan" for years.
      #   effective_io_concurrency 1
      #                               the default: one outstanding read at a time. An SSD can have
      #                               dozens, and this is what decides whether the planner believes
      #                               a bitmap heap scan is viable at all.
      #
      # THE TIMEOUTS, 2026-10-20. All three were 0, i.e. unbounded:
      #
      #   statement_timeout 30s          one bad query holds a connection and a snapshot for as
      #                                   long as it likes.
      #   lock_timeout 10s               a statement that waits for a lock fails fast and is
      #                                   retried, rather than sitting in the queue holding the
      #                                   lock it is waiting for.
      #   idle_in_transaction_session_timeout 60s
      #                                   THE IMPORTANT ONE. A connection that opens a transaction
      #                                   and walks away holds its snapshot, and that is what pins
      #                                   the vacuum horizon and stops VACUUM from reclaiming
      #                                   dead rows ANYWHERE in the database. The application opens
      #                                   explicit transactions in eight places in FulfillmentService
      #                                   alone, so a leaked one is a matter of time.
      #
      # THE LOGS, 2026-10-20. Three settings that cost nothing and say what used to be invisible:
      # a query that spills to disk (log_temp_files), a lock wait (log_lock_waits), and an
      # autovacuum that ran for two minutes and changed the plan underneath a query
      # (log_autovacuum_min_duration, which was 10 minutes - i.e. off in practice).
      #
      # AND THE TWO THAT NEED A RESTART, which is why they are here rather than in the file
      # the settings above came from: shared_buffers 256M (128M was the PostgreSQL default, not a
      # choice, and a quarter of a 512M container belongs to the buffer cache) and
      # max_connections 50 (100 on a 512M container is generous; the application's pool is
      # capped at 5, so this is insurance against a connection storm).
      #
      # WHY THEY ARE IN THE COMPOSE FILE AND NOT ONLY ON THE BOX. They were applied to the
      # running server with ALTER SYSTEM, which writes postgresql.auto.conf - and that file lives
      # in the container's volume. A box rebuilt from the compose file, or a disaster recovery on
      # a new server, would come up with PostgreSQL's DEFAULTS, and every one of these would
      # silently be back to what it was. A setting that only exists on one machine is not a
      # setting; it is an incident waiting for a rebuild.
"""

NEW_COMMAND = """      command:
        - postgres
        - -c
        - effective_cache_size=1GB
        - -c
        - shared_preload_libraries=pg_stat_statements
        - -c
        - log_min_duration_statement=1000
        - -c
        - track_io_timing=on
        - -c
        - random_page_cost=1.1
        - -c
        - effective_io_concurrency=200
        - -c
        - statement_timeout=30s
        - -c
        - lock_timeout=10s
        - -c
        - idle_in_transaction_session_timeout=60s
        - -c
        - log_temp_files=10MB
        - -c
        - log_lock_waits=on
        - -c
        - log_autovacuum_min_duration=1000
        - -c
        - shared_buffers=256MB
        - -c
        - max_connections=50"""

for path, container in (('deploy/docker-compose.prod.yml', 'postgres'),
                        ('deploy/docker-compose.staging.yml', 'staging-postgres')):
    src = open(path, encoding='utf-8').read()

    # 1. the command block
    old_cmd = """      command:
        - postgres
        - -c
        - effective_cache_size=1GB
        - -c
        - shared_preload_libraries=pg_stat_statements
        - -c
        - log_min_duration_statement=1000
        - -c
        - track_io_timing=on"""
    assert src.count(old_cmd) == 1, (path, src.count(old_cmd))
    src = src.replace(old_cmd, NEW_COMMAND, 1)

    # 2. the tuning comment above it
    old_c = """      # Postgres tuning and query visibility. Measured on this box 2026-10-19 and left as it was found:"""
    assert src.count(old_c) == 1, (path, src.count(old_c))
    src = src.replace(old_c, TUNING, 1)

    # 3. the initdb args: add --data-checksums
    old_i = 'POSTGRES_INITDB_ARGS: "--locale-provider=icu --icu-locale=uk-UA --locale=C.UTF-8"'
    assert src.count(old_i) == 1, (path, src.count(old_i))
    new_i = ('POSTGRES_INITDB_ARGS: "--locale-provider=icu --icu-locale=uk-UA --locale=C.UTF-8 '
             '--data-checksums"')
    src = src.replace(old_i, new_i, 1)

    open(path, 'w', encoding='utf-8', newline='').write(src)
    print('updated', path)
