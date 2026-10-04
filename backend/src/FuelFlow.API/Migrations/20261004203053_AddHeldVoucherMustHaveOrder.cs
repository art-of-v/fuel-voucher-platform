using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Makes it impossible for a voucher to be in somebody's hands with no order behind it. This is
    /// the rule the whole issuance-orders change exists for: a voucher belongs to an order once it
    /// is handed over (purchase, renewal replacement, company issuance), and to no order while it
    /// is still warehouse stock.
    /// </summary>
    public partial class AddHeldVoucherMustHaveOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop-then-ADD rather than a DO $$ block: Postgres has no ADD CONSTRAINT IF NOT EXISTS,
            // and a DO block would break `dotnet ef migrations script --idempotent`, which wraps
            // every statement in its own DO $EF$ block and cannot nest PL/pgSQL. Same reasoning as
            // the AddForeignKey helper in 20260919113317_AddMissingDriftedTables.
            migrationBuilder.Sql(
                """ALTER TABLE "fuel_vouchers" DROP CONSTRAINT IF EXISTS "ck_voucher_held_has_order";""");

            migrationBuilder.Sql(
                """
                ALTER TABLE "fuel_vouchers" ADD CONSTRAINT "ck_voucher_held_has_order"
                CHECK (status NOT IN ('Assigned', 'Used', 'Blocked') OR "order_id" IS NOT NULL)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """ALTER TABLE "fuel_vouchers" DROP CONSTRAINT IF EXISTS "ck_voucher_held_has_order";""");
        }
    }
}
