using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// A supplier's registry code (ЄДРОПУ / ІПН) becomes mandatory and unique.
    /// <para>
    /// Mandatory on the business's instruction (2026-10-20): the code is what a VAT invoice is
    /// issued against, so a supplier without one cannot be invoiced and the business carries the
    /// cost of that. Unique because two rows with the same code are the same counterparty
    /// recorded twice - and a business that pays "two" suppliers who are one person pays twice
    /// and reconciles the difference by hand.
    /// </para>
    /// <para>
    /// <b>The migration refuses to run on incomplete data, and it says which rows.</b> EF's
    /// scaffold for a NOT NULL change fills the column with an empty string, which would put a
    /// blank where a registry code belongs - a value that is not a registry code at all, stored
    /// as if it were one. This one checks first and raises instead, naming the suppliers, so the
    /// person who has to fix it knows who to fix.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class SupplierEdrIpnIsMandatory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The check first. A blank is as bad as a NULL here - the column is a registry code,
            // and "" is not one.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    missing text;
                BEGIN
                    SELECT string_agg(name, ', ' ORDER BY name)
                    INTO missing
                    FROM suppliers
                    WHERE edr_ipn IS NULL OR btrim(edr_ipn) = '';

                    IF missing IS NOT NULL THEN
                        RAISE EXCEPTION
                            'Cannot make suppliers.edr_ipn mandatory: % supplier(s) have no registry code (%). The code is what a VAT invoice is issued against, so it cannot be filled with a placeholder - enter it on the supplier instead.',
                            (SELECT count(*) FROM suppliers WHERE edr_ipn IS NULL OR btrim(edr_ipn) = ''),
                            missing;
                    END IF;
                END $$;
                """);

            // No default value, and deliberately so: a default would put an empty string where
            // the registry code belongs, and the check above has already established that there
            // is nothing to fill.
            migrationBuilder.Sql("""
                ALTER TABLE "suppliers" ALTER COLUMN "edr_ipn" SET NOT NULL;
                """);

            // The format, in the database as well as in the API. The API refuses a bad value
            // with a sentence a person can act on; the database refuses one that arrived by any
            // other road, because a registry code that is not a registry code is not a registry
            // code.
            migrationBuilder.Sql("""
                ALTER TABLE "suppliers" ADD CONSTRAINT "ck_suppliers_edr_ipn_format"
                CHECK ("edr_ipn" ~ '^[0-9]{8,10}$');
                """);

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_edr_ipn",
                table: "suppliers",
                column: "edr_ipn",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_suppliers_edr_ipn",
                table: "suppliers");

            migrationBuilder.Sql("""
                ALTER TABLE "suppliers" DROP CONSTRAINT IF EXISTS "ck_suppliers_edr_ipn_format";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "suppliers" ALTER COLUMN "edr_ipn" DROP NOT NULL;
                """);
        }
    }
}
