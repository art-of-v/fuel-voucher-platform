using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// The ledger's keys become time-ordered.
    /// <para>
    /// Every other key in the schema is a UUIDv7 - a timestamp followed by randomness - and the
    /// ledger was the one place that was not: <c>journal_entries</c>, <c>journal_lines</c> and
    /// <c>accounting_periods</c> were written with <c>Guid.NewGuid()</c>, which is random. A random
    /// key on the fastest-growing table in the ledger is not a cosmetic difference: every insert
    /// lands on a random leaf of the B-tree, the index is written across the whole key space
    /// instead of at its right-hand edge, and the pages churn. The application now writes
    /// <c>Guid.CreateVersion7()</c> for all three; this migration brings the rows that are already
    /// there onto the same convention.
    /// </para>
    /// <para>
    /// A key says nothing about the row it names, so rewriting it is allowed - but only if the
    /// rewrite keeps the accounting history intact: every entry keeps its lines, every entry
    /// still balances, and every foreign key still points at the row it pointed at. That is what
    /// this migration is careful about, and what the test beside it asserts.
    /// </para>
    /// <para>
    /// <b>What is NOT rewritten:</b> the entry's own content - its number, its date, its amounts,
    /// its tamper hash - and the periods' dates. The tamper hash is a checksum over the entry's
    /// content and never over its key, so it stays true.
    /// </para>
    /// </summary>
    public partial class TheLedgerKeysAreTimeOrdered : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The uuid builder. PostgreSQL 16 - the version this platform runs - has no uuidv7(),
            // so the layout is spelled out here, and it is the layout RFC 9562 describes:
            //
            //   48 bits   unix_ts_ms      the timestamp, and the reason the key sorts
            //    4 bits   version = 7
            //   12 bits   rand_a
            //    2 bits   variant = 10
            //   62 bits   rand_b
            //
            // The function stays in the database after the migration: it is the platform's key
            // convention written where the seed scripts and the future migrations can reach it.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION fuelflow_uuidv7(p_at timestamptz)
                RETURNS uuid
                LANGUAGE plpgsql
                VOLATILE
                AS $fuelflow$
                DECLARE
                    v_ms     bigint;
                    v_ts     text;
                    v_rand_a text;
                    v_rand_b text;
                BEGIN
                    -- The timestamp is the FIRST 48 bits, so the key sorts by the moment the row
                    -- was written. greatest() guards the epoch: a negative one has no place in a
                    -- 48-bit field, and the only rows that could carry one are rows written
                    -- before 1970.
                    v_ms := greatest(floor(extract(epoch FROM p_at) * 1000)::bigint, 0);
                    v_ts := lpad(to_hex(v_ms), 12, '0');

                    -- 12 bits of rand_a and 60 bits of rand_b. The other two bits of rand_b are
                    -- the variant, which the 17th hex digit below carries.
                    v_rand_a := lpad(to_hex(floor(random() * 4096)::bigint), 3, '0');
                    v_rand_b := lpad(to_hex(floor(random() * 1152921504606846976)::bigint), 15, '0');

                    -- The 13th hex digit is the version nibble and the 17th is the variant nibble.
                    -- '8' and '9' are the two nibbles whose first two bits are '10', which is the
                    -- RFC 4122 variant; anything else would be a key of a different kind.
                    RETURN (v_ts || '7' || v_rand_a
                            || CASE WHEN random() < 0.5 THEN '8' ELSE '9' END
                            || v_rand_b)::uuid;
                END;
                $fuelflow$;
                """);

            // The new key of a row is built from the moment the row was WRITTEN, not from the
            // moment this migration runs. An entry written in March has to sort before one
            // written in April, and a key stamped "now" would put the whole history at the end
            // of the index - the same churn this migration exists to remove.
            ReKeyTheLedger(
                migrationBuilder,
                periodKey: "fuelflow_uuidv7(period_start::timestamp AT TIME ZONE 'UTC')",
                entryKey: "fuelflow_uuidv7(created_at_utc)",
                lineKey: "fuelflow_uuidv7(e.created_at_utc)",
                sequenceKey: "fuelflow_uuidv7(now())");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The keys cannot be un-written. The ids the rows had before this migration are
            // gone - they were replaced, not remembered - and the code this migration is being
            // rolled back to writes random ones. So the Down gives every row a fresh random key
            // and moves every reference with it, which is the state the previous code expects
            // to find.
            //
            // The rows themselves are untouched: same amounts, same numbers, same dates, same
            // balances. Only the opaque key they are addressed by changes, which is the one
            // thing a key is allowed to be.
            ReKeyTheLedger(
                migrationBuilder,
                periodKey: "gen_random_uuid()",
                entryKey: "gen_random_uuid()",
                lineKey: "gen_random_uuid()",
                sequenceKey: "gen_random_uuid()");
        }

        /// <summary>
        /// Rewrites the accounting tables' keys and moves every reference to them: the three
        /// tables of the journal, and the sequence that numbers its entries. The four
        /// expressions are what a row's new key is built from - one per table - so the same
        /// machinery serves the time-ordered rewrite and the random one.
        /// </summary>
        private static void ReKeyTheLedger(
            MigrationBuilder migrationBuilder,
            string periodKey,
            string entryKey,
            string lineKey,
            string sequenceKey)
        {
            // The rows are re-keyed through three maps rather than in place, because the keys of
            // one table are the keys another table points at: a line points at its entry, an
            // entry points at its period and at the entry that reverses it. The maps say what
            // each old key becomes, and every reference is moved with it.
            migrationBuilder.Sql($"""
                CREATE TEMP TABLE ledger_period_keys (
                    old_id uuid PRIMARY KEY,
                    new_id uuid NOT NULL
                );

                INSERT INTO ledger_period_keys (old_id, new_id)
                SELECT id, {periodKey}
                  FROM accounting_periods;
                """);

            migrationBuilder.Sql($"""
                CREATE TEMP TABLE ledger_entry_keys (
                    old_id uuid PRIMARY KEY,
                    new_id uuid NOT NULL
                );

                INSERT INTO ledger_entry_keys (old_id, new_id)
                SELECT id, {entryKey}
                  FROM journal_entries;
                """);

            migrationBuilder.Sql($"""
                CREATE TEMP TABLE ledger_line_keys (
                    old_id uuid PRIMARY KEY,
                    new_id uuid NOT NULL
                );

                -- A line has no timestamp of its own, so it takes the entry's: the lines of one
                -- entry then sit next to each other in the index, which is where a reader of
                -- one entry looks for them.
                INSERT INTO ledger_line_keys (old_id, new_id)
                SELECT l.id, {lineKey}
                  FROM journal_lines l
                  JOIN journal_entries e ON e.id = l.entry_id;
                """);

            // The sequences get their own map: a sequence has no timestamp of its own either -
            // the table is a name and a counter - so its key is stamped with the moment of the
            // rewrite, which is the closest thing to the moment the row was written that the
            // table still knows.
            migrationBuilder.Sql($"""
                CREATE TEMP TABLE ledger_sequence_keys (
                    old_id uuid PRIMARY KEY,
                    new_id uuid NOT NULL
                );

                INSERT INTO ledger_sequence_keys (old_id, new_id)
                SELECT id, {sequenceKey}
                  FROM document_sequences;
                """);

            // The three foreign keys are dropped for the length of the rewrite and added back
            // unchanged at the end of it. They are the reason the rewrite cannot simply be an
            // UPDATE: a key that is still referenced cannot be changed while the reference is
            // checked, and the references are what is being changed.
            //
            // The definitions that come back are the ones the model declares - ON DELETE
            // RESTRICT, no ON UPDATE clause - so the schema this migration leaves behind is the
            // schema it found.
            migrationBuilder.Sql("""
                ALTER TABLE journal_entries
                    DROP CONSTRAINT "FK_journal_entries_accounting_periods_period_id";
                ALTER TABLE journal_entries
                    DROP CONSTRAINT "FK_journal_entries_journal_entries_reversed_by_entry_id";
                ALTER TABLE journal_lines
                    DROP CONSTRAINT "FK_journal_lines_journal_entries_entry_id";
                """);

            // Two guards stand in the way, and both are about the CONTENT of a posting rather
            // than about its key:
            //
            //   trg_journal_lines_posted_is_immutable refuses any change to the lines of a
            //   posted entry - and this migration changes the key of every line.
            //   trg_journal_entry_posted_is_coherent refuses a posting into a CLOSED period -
            //   and this migration touches every posted entry, including the ones in a closed
            //   month, whose content it does not change at all.
            //
            // They are switched off for the rewrite and switched on again immediately after
            // it, inside the same transaction. What they protect - a posted entry is never
            // edited, a closed month takes no new postings - is not what happens here: no
            // amount, no date, no number and no status is touched.
            migrationBuilder.Sql("""
                ALTER TABLE journal_entries DISABLE TRIGGER trg_journal_entry_posted_is_coherent;
                ALTER TABLE journal_lines DISABLE TRIGGER trg_journal_lines_posted_is_immutable;
                """);

            // The rewrite itself. The order is the foreign keys' order: a period, then the
            // entries in it, then their lines - so that every reference is moved to a key that
            // already exists when it is moved.
            migrationBuilder.Sql("""
                UPDATE accounting_periods p
                   SET id = k.new_id
                  FROM ledger_period_keys k
                 WHERE k.old_id = p.id;
                """);

            // An entry moves its own key, the period it belongs to and the entry that reverses
            // it - one statement each.
            //
            // Three statements rather than one, and that is not a style choice. PostgreSQL
            // transforms an UPDATE's FROM clause BEFORE it puts the target relation into the
            // namespace, so a join condition in the FROM clause cannot mention the row being
            // updated: "invalid reference to FROM-clause entry for table e". The columns the
            // entry points at therefore move in statements of their own, each joining the row
            // to the map on a column the previous statement did not touch.
            //
            // The order inside the set matters the same way: the entry's own key moves first,
            // and the references it holds still hold the OLD keys of the rows they point at -
            // which is what the maps are keyed by.
            migrationBuilder.Sql("""
                UPDATE journal_entries e
                   SET id = k.new_id
                  FROM ledger_entry_keys k
                 WHERE k.old_id = e.id;
                """);

            migrationBuilder.Sql("""
                UPDATE journal_entries e
                   SET period_id = p.new_id
                  FROM ledger_period_keys p
                 WHERE p.old_id = e.period_id;
                """);

            // The entry that reverses this one. A NULL reference matches nothing in the map,
            // and a NULL reference is what an entry that reverses nothing holds - so the row
            // is left alone, which is the same thing the map says about it.
            migrationBuilder.Sql("""
                UPDATE journal_entries e
                   SET reversed_by_entry_id = r.new_id
                  FROM ledger_entry_keys r
                 WHERE r.old_id = e.reversed_by_entry_id;
                """);

            migrationBuilder.Sql("""
                UPDATE journal_lines l
                   SET id = k.new_id
                  FROM ledger_line_keys k
                 WHERE k.old_id = l.id;
                """);

            migrationBuilder.Sql("""
                UPDATE journal_lines l
                   SET entry_id = e.new_id
                  FROM ledger_entry_keys e
                 WHERE e.old_id = l.entry_id;
                """);

            // The sequence that hands out the entry numbers is NAMED after the period it
            // belongs to - "journal:<period id>" - so a period that changes its key changes the
            // name of its sequence with it.
            //
            // Without this the next posting into that period would start the count again at 1
            // and collide with the entry number 1 the period already holds: the unique index
            // on (period_id, entry_no) would refuse it, and the ledger would be stuck for the
            // rest of the month.
            migrationBuilder.Sql("""
                UPDATE document_sequences s
                   SET name = 'journal:' || k.new_id::text
                  FROM ledger_period_keys k
                 WHERE s.name = 'journal:' || k.old_id::text;
                """);

            // The sequence's OWN key moves too, so that the table holds one shape of key and
            // not two: the rows that are there were written with a random key, and the
            // application writes a time-ordered one for every row it adds from now on. A table
            // whose key shape is decided by which deploy wrote the row is the thing F-29 was
            // about, and this table is small enough that the cost of getting it right is
            // one statement.
            //
            // Nothing points at a sequence's id - the application finds its counter by NAME -
            // so the key can move on its own, with no reference to follow.
            migrationBuilder.Sql("""
                UPDATE document_sequences s
                   SET id = k.new_id
                  FROM ledger_sequence_keys k
                 WHERE k.old_id = s.id;
                """);

            // The balance is checked NOW, rather than at the end of the transaction.
            //
            // The two balance triggers are CONSTRAINT triggers, deferred to the end of the
            // transaction - and PostgreSQL refuses an ALTER TABLE on a table that has trigger
            // events still pending ("cannot ALTER TABLE journal_entries because it has pending
            // trigger events"). The statements below are ALTER TABLEs, so the events have to
            // have been consumed first.
            //
            // Firing them here is also the RIGHT moment to fire them: every key has been
            // rewritten by now and every line points at the entry it pointed at, so the check
            // sees the finished ledger rather than a half-moved one. A rewrite that unbalances
            // an entry fails the migration here, loudly, and rolls the whole thing back.
            migrationBuilder.Sql("SET CONSTRAINTS ALL IMMEDIATE;");

            // The guards come back, and so do the foreign keys.
            migrationBuilder.Sql("""
                ALTER TABLE journal_entries ENABLE TRIGGER trg_journal_entry_posted_is_coherent;
                ALTER TABLE journal_lines ENABLE TRIGGER trg_journal_lines_posted_is_immutable;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE journal_entries
                    ADD CONSTRAINT "FK_journal_entries_accounting_periods_period_id"
                    FOREIGN KEY (period_id) REFERENCES accounting_periods (id) ON DELETE RESTRICT;
                ALTER TABLE journal_entries
                    ADD CONSTRAINT "FK_journal_entries_journal_entries_reversed_by_entry_id"
                    FOREIGN KEY (reversed_by_entry_id) REFERENCES journal_entries (id) ON DELETE RESTRICT;
                ALTER TABLE journal_lines
                    ADD CONSTRAINT "FK_journal_lines_journal_entries_entry_id"
                    FOREIGN KEY (entry_id) REFERENCES journal_entries (id) ON DELETE RESTRICT;
                """);

            // The maps have served their purpose. Dropping them explicitly rather than leaving
            // them to ON COMMIT DROP, because the connection they were made on goes back into
            // the pool afterwards - and a pooled connection that still carries a table is a
            // surprise for whoever borrows it next.
            migrationBuilder.Sql("""
                DROP TABLE ledger_sequence_keys;
                DROP TABLE ledger_line_keys;
                DROP TABLE ledger_entry_keys;
                DROP TABLE ledger_period_keys;
                """);
        }
    }
}
