using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheLastTwoIntegerKeysBecomeUuids : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The last two tables in the schema whose key was still a counter.
            //
            // A key is supposed to say nothing. A number taken from a sequence says how many
            // rows came before it, which is a fact about the past that a reader does not need
            // and cannot un-see - and it makes the key a thing two databases can disagree about
            // (a restored dump and a live database both claim the same next number).
            //
            // NOTHING REFERENCES THESE IDS. No foreign key points at fulfillments(id) or at
            // outbox_events(id) - the tables are leaves, and the columns that reference them
            // are named (order_id, voucher_id). That is what makes this a conversion and not a
            // rewrite of half the schema: the old ids have no one to be carried over to.
            //
            // The rows keep their meaning and get a new key. The old key is not mapped, because
            // a mapping would be a rule nobody can check and nobody needs: the id said nothing,
            // and now it says nothing again. What the rows ARE - a fulfilment of this order for
            // this voucher at this time, an outbox event of this type with this payload - is
            // untouched.
            //
            // The log lines that name an id will name the new one, and the old number is not
            // resolvable afterwards. That is the point: the old number was a counter, and a
            // counter is not a fact about the row.
            //
            // The order matters: the new column has to exist and be filled before the old one
            // goes, and the primary key has to go with the column it was on.
            foreach (var table in new[] { "fulfillments", "outbox_events" })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD COLUMN id_new uuid;
                    """);

                migrationBuilder.Sql($"""
                    UPDATE {table} SET id_new = gen_random_uuid();
                    """);

                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ALTER COLUMN id_new SET NOT NULL;
                    """);

                migrationBuilder.Sql($"""
                    ALTER TABLE {table} DROP CONSTRAINT "PK_{table}";
                    """);

                migrationBuilder.Sql($"""
                    ALTER TABLE {table} DROP COLUMN id;
                    """);

                migrationBuilder.Sql($"""
                    ALTER TABLE {table} RENAME COLUMN id_new TO id;
                    """);

                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD CONSTRAINT "PK_{table}" PRIMARY KEY (id);
                    """);

                // The sequence went with the column it numbered.
                migrationBuilder.Sql($"""
                    DROP SEQUENCE IF EXISTS {table}_id_seq;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to a counter. The ids are new - they are uuids now and they were never
            // anything else - so going back means giving every row a fresh number, and the
            // numbers it used to have are not recoverable. The rows are.
            foreach (var table in new[] { "fulfillments", "outbox_events" })
            {
                migrationBuilder.Sql($"""
                    CREATE SEQUENCE {table}_id_seq;
                    """);

                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD COLUMN id_old integer;
                    """);

                migrationBuilder.Sql($"""
                    UPDATE {table} SET id_old = nextval('{table}_id_seq');
                    """);

                migrationBuilder.Sql($"""
                    ALTER TABLE {table} DROP CONSTRAINT "PK_{table}";
                    """);

                migrationBuilder.Sql($"""
                    ALTER TABLE {table} DROP COLUMN id;
                    """);

                migrationBuilder.Sql($"""
                    ALTER TABLE {table} RENAME COLUMN id_old TO id;
                    """);

                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD CONSTRAINT "PK_{table}" PRIMARY KEY (id);
                    """);

                migrationBuilder.Sql($"""
                    ALTER SEQUENCE {table}_id_seq OWNED BY {table}.id;
                    """);
            }
        }
    }
}
