using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class FuelTypeIdsBecomeUuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A fuel's id stops being the natural key and becomes an opaque uuid. The brand's own
            // code for the grade lives in brand_fuel_code, and it is what the importers match on.
            //
            // The rows that carry the old spelling - the ones the seed wrote, "okko-95" and its
            // neighbours - are MAPPED, not deleted. EF's scaffold wanted to delete them, because an id
            // that changes looks like a delete plus an insert, and deleting the catalogue because a
            // column changed type is not a migration anyone wants to read about.
            //
            // The mapping is the one FuelIdMapper applies in C#: md5('fuelflow-fuel:' || id)::uuid.
            // The two have to agree, or a row written before the conversion and a row written after it
            // would be two different fuels with the same name - which is asserted against a real
            // database, because that is the only way to hold two languages to one rule.

            // Every id that appears anywhere, mapped once. The four tables without a foreign key can
            // hold an id no fuel has, and those are mapped by the same rule rather than left to fail
            // the ALTER: an orphan stays an orphan, which is what it already was.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE fuel_id_map ON COMMIT DROP AS
                WITH all_ids AS (
                    SELECT id AS old_id FROM fuel_types
                    UNION SELECT fuel_type_id FROM fuel_nominals WHERE fuel_type_id IS NOT NULL
                    UNION SELECT fuel_type_id FROM fuel_price_history WHERE fuel_type_id IS NOT NULL
                    UNION SELECT fuel_type_id FROM fuel_type_product_codes WHERE fuel_type_id IS NOT NULL
                    UNION SELECT fuel_type_id FROM fuel_vouchers WHERE fuel_type_id IS NOT NULL
                    UNION SELECT fuel_type_id FROM order_line_items WHERE fuel_type_id IS NOT NULL
                    UNION SELECT fuel_type_id FROM purchase_batches WHERE fuel_type_id IS NOT NULL
                    UNION SELECT fuel_type_id FROM voucher_exchanges WHERE fuel_type_id IS NOT NULL
                )
                SELECT
                    old_id,
                    CASE
                        WHEN old_id ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                            THEN old_id::uuid
                        ELSE md5('fuelflow-fuel:' || old_id)::uuid
                    END AS new_id
                FROM all_ids;
                """);

            // The length checks are gone with the text: a uuid has no length to bound. They are
            // dropped before the columns change, because a check over a text column cannot survive it.
            migrationBuilder.Sql("""ALTER TABLE fuel_types DROP CONSTRAINT IF EXISTS ck_fuel_types_id_length;""");
            migrationBuilder.Sql("""ALTER TABLE fuel_price_history DROP CONSTRAINT IF EXISTS ck_fuel_price_history_fuel_type_id_length;""");
            migrationBuilder.Sql("""ALTER TABLE fuel_type_product_codes DROP CONSTRAINT IF EXISTS ck_fuel_type_product_codes_fuel_type_id_length;""");
            migrationBuilder.Sql("""ALTER TABLE fuel_vouchers DROP CONSTRAINT IF EXISTS ck_fuel_vouchers_fuel_type_id_length;""");
            migrationBuilder.Sql("""ALTER TABLE purchase_batches DROP CONSTRAINT IF EXISTS ck_purchase_batches_fuel_type_id_length;""");
            migrationBuilder.Sql("""ALTER TABLE voucher_exchanges DROP CONSTRAINT IF EXISTS ck_voucher_exchanges_fuel_type_id_length;""");

            // A voucher's default fuel id was the empty string, which is what "no fuel" looked like in
            // a text column. A uuid has no empty string, so the default goes with the type: the
            // honest default for a fuel id is none, and the column stays NOT NULL - which means an
            // insert without a fuel is refused rather than filed under a fuel that does not exist.
            migrationBuilder.Sql("""ALTER TABLE fuel_vouchers ALTER COLUMN fuel_type_id DROP DEFAULT;""");

            // The foreign keys point at fuel_types(id), and Postgres refuses to change the type of a
            // column a foreign key refers to. They are dropped and put back below, unchanged in
            // everything but the type they point at.
            migrationBuilder.Sql("""ALTER TABLE fuel_nominals DROP CONSTRAINT IF EXISTS "FK_fuel_nominals_fuel_types_fuel_type_id";""");
            migrationBuilder.Sql("""ALTER TABLE fuel_price_history DROP CONSTRAINT IF EXISTS "FK_fuel_price_history_fuel_types_fuel_type_id";""");
            migrationBuilder.Sql("""ALTER TABLE fuel_type_product_codes DROP CONSTRAINT IF EXISTS "FK_fuel_type_product_codes_fuel_types_fuel_type_id";""");
            migrationBuilder.Sql("""ALTER TABLE fuel_vouchers DROP CONSTRAINT IF EXISTS "FK_fuel_vouchers_fuel_types_fuel_type_id";""");

            // The mapping, then the type. The mapping first, because a uuid cannot be written into a
            // text column that is about to become a uuid column - the update writes the uuid as text
            // and the ALTER reads it back.
            foreach (var table in new[]
                     {
                         "fuel_nominals",
                         "fuel_price_history",
                         "fuel_type_product_codes",
                         "fuel_vouchers",
                         "order_line_items",
                         "purchase_batches",
                         "voucher_exchanges",
                     })
            {
                migrationBuilder.Sql($"""
                    UPDATE {table} t
                    SET fuel_type_id = m.new_id
                    FROM fuel_id_map m
                    WHERE t.fuel_type_id = m.old_id;
                    """);
                migrationBuilder.Sql($"""
                    ALTER TABLE {table}
                    ALTER COLUMN fuel_type_id TYPE uuid USING fuel_type_id::uuid;
                    """);
            }

            migrationBuilder.Sql("""
                UPDATE fuel_types f
                SET id = m.new_id
                FROM fuel_id_map m
                WHERE f.id = m.old_id;
                """);
            migrationBuilder.Sql("""ALTER TABLE fuel_types ALTER COLUMN id TYPE uuid USING id::uuid;""");

            // The foreign keys again, now uuid to uuid.
            migrationBuilder.Sql("""
                ALTER TABLE fuel_nominals
                ADD CONSTRAINT "FK_fuel_nominals_fuel_types_fuel_type_id"
                FOREIGN KEY (fuel_type_id) REFERENCES fuel_types(id) ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE fuel_price_history
                ADD CONSTRAINT "FK_fuel_price_history_fuel_types_fuel_type_id"
                FOREIGN KEY (fuel_type_id) REFERENCES fuel_types(id) ON DELETE RESTRICT;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE fuel_type_product_codes
                ADD CONSTRAINT "FK_fuel_type_product_codes_fuel_types_fuel_type_id"
                FOREIGN KEY (fuel_type_id) REFERENCES fuel_types(id) ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE fuel_vouchers
                ADD CONSTRAINT "FK_fuel_vouchers_fuel_types_fuel_type_id"
                FOREIGN KEY (fuel_type_id) REFERENCES fuel_types(id) ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The natural keys are NOT recoverable: the row said "okko-95" and now says the uuid
            // derived from that name, and the name is not stored anywhere. The Down therefore goes
            // back to a text column carrying the uuid as text, which is lossy in one direction only -
            // and that is stated here rather than discovered later.
            migrationBuilder.Sql("""ALTER TABLE fuel_vouchers DROP CONSTRAINT IF EXISTS "FK_fuel_vouchers_fuel_types_fuel_type_id";""");
            migrationBuilder.Sql("""ALTER TABLE fuel_type_product_codes DROP CONSTRAINT IF EXISTS "FK_fuel_type_product_codes_fuel_types_fuel_type_id";""");
            migrationBuilder.Sql("""ALTER TABLE fuel_price_history DROP CONSTRAINT IF EXISTS "FK_fuel_price_history_fuel_types_fuel_type_id";""");
            migrationBuilder.Sql("""ALTER TABLE fuel_nominals DROP CONSTRAINT IF EXISTS "FK_fuel_nominals_fuel_types_fuel_type_id";""");

            foreach (var table in new[]
                     {
                         "fuel_nominals",
                         "fuel_price_history",
                         "fuel_type_product_codes",
                         "fuel_vouchers",
                         "order_line_items",
                         "purchase_batches",
                         "voucher_exchanges",
                     })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table}
                    ALTER COLUMN fuel_type_id TYPE text USING fuel_type_id::text;
                    """);
            }

            migrationBuilder.Sql("""ALTER TABLE fuel_types ALTER COLUMN id TYPE text USING id::text;""");

            migrationBuilder.Sql("""
                ALTER TABLE fuel_nominals
                ADD CONSTRAINT "FK_fuel_nominals_fuel_types_fuel_type_id"
                FOREIGN KEY (fuel_type_id) REFERENCES fuel_types(id) ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE fuel_price_history
                ADD CONSTRAINT "FK_fuel_price_history_fuel_types_fuel_type_id"
                FOREIGN KEY (fuel_type_id) REFERENCES fuel_types(id) ON DELETE RESTRICT;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE fuel_type_product_codes
                ADD CONSTRAINT "FK_fuel_type_product_codes_fuel_types_fuel_type_id"
                FOREIGN KEY (fuel_type_id) REFERENCES fuel_types(id) ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE fuel_vouchers
                ADD CONSTRAINT "FK_fuel_vouchers_fuel_types_fuel_type_id"
                FOREIGN KEY (fuel_type_id) REFERENCES fuel_types(id) ON DELETE RESTRICT;
                """);

            // The empty-string default comes back with the text it belonged to.
            migrationBuilder.Sql("""ALTER TABLE fuel_vouchers ALTER COLUMN fuel_type_id SET DEFAULT ''::text;""");

            // The length checks come back with the text they bounded.
            migrationBuilder.Sql("""ALTER TABLE fuel_types ADD CONSTRAINT ck_fuel_types_id_length CHECK (length(id) <= 100);""");
            migrationBuilder.Sql("""ALTER TABLE fuel_price_history ADD CONSTRAINT ck_fuel_price_history_fuel_type_id_length CHECK (length(fuel_type_id) <= 100);""");
            migrationBuilder.Sql("""ALTER TABLE fuel_type_product_codes ADD CONSTRAINT ck_fuel_type_product_codes_fuel_type_id_length CHECK (length(fuel_type_id) <= 100);""");
            migrationBuilder.Sql("""ALTER TABLE fuel_vouchers ADD CONSTRAINT ck_fuel_vouchers_fuel_type_id_length CHECK (length(fuel_type_id) <= 100);""");
            migrationBuilder.Sql("""ALTER TABLE purchase_batches ADD CONSTRAINT ck_purchase_batches_fuel_type_id_length CHECK (length(fuel_type_id) <= 100);""");
            migrationBuilder.Sql("""ALTER TABLE voucher_exchanges ADD CONSTRAINT ck_voucher_exchanges_fuel_type_id_length CHECK (length(fuel_type_id) <= 100);""");
        }
    }
}
