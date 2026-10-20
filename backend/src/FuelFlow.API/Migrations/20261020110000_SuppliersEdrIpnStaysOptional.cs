using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// The supplier's registry code goes back to being optional.
    /// <para>
    /// The migration before this one made it NOT NULL. It was applied to staging before the
    /// business said that a supplier can be recorded before anyone has looked the code up, so
    /// staging is the one place where the column is still mandatory - and a supplier entered
    /// there would be refused.
    /// </para>
    /// <para>
    /// This drops the constraint again, which is a no-op on a database where the column was
    /// never made mandatory. It is here because the history is append-only: the earlier
    /// migration has already run in one environment, and the only honest way back is another
    /// migration.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class SuppliersEdrIpnStaysOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The blanks first, for the same reason as in the migration before: a blank is not a
            // registry code, and the API stores one as NULL. The rows that carry a blank are the
            // ones written before that rule.
            migrationBuilder.Sql("""
                UPDATE "suppliers" SET "edr_ipn" = NULL
                WHERE "edr_ipn" IS NOT NULL AND btrim("edr_ipn") = '';
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "suppliers" ALTER COLUMN "edr_ipn" DROP NOT NULL;
                """);

            // The constraint is replaced, not left as it is. The one this database carries was
            // written before the column was allowed to be empty, so it refuses a NULL - and a
            // supplier without a code is exactly what the business allows. Replaced with the
            // version that says the same thing as the model: no code is fine, a code has to be
            // a code.
            migrationBuilder.Sql("""
                ALTER TABLE "suppliers" DROP CONSTRAINT IF EXISTS "ck_suppliers_edr_ipn_format";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "suppliers" ADD CONSTRAINT "ck_suppliers_edr_ipn_format"
                CHECK ("edr_ipn" IS NULL OR "edr_ipn" ~ '^[0-9]{8,10}$');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The reverse would refuse on any supplier without a code - which is the whole
            // point of the rule. It is written anyway, because a Down that cannot run is
            // worse than one that fails loudly: the rollback says what is missing and
            // who has to fill it in.
            migrationBuilder.Sql("""
                ALTER TABLE "suppliers" ALTER COLUMN "edr_ipn" SET NOT NULL;
                """);
        }
    }
}