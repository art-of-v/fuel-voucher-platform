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
            migrationBuilder.Sql("""
                ALTER TABLE "suppliers" ALTER COLUMN "edr_ipn" DROP NOT NULL;
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