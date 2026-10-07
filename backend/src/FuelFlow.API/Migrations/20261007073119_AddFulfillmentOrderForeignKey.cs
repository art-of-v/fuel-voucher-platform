using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations;

/// <summary>
/// Adds the foreign key from a fulfillment to its order, which the database never actually had.
/// </summary>
/// <remarks>
/// <para>
/// <c>fulfillments.order_id</c> exists in the EF model snapshot with
/// <c>ON DELETE CASCADE</c> - it has been there since a folder-rename commit carried the snapshot
/// along - but <b>no migration ever emitted it</b>, so the database has no such constraint while EF
/// believes it is applied. That is why <c>dotnet ef migrations add</c> produces an empty migration:
/// model and database silently disagree, and the gap is invisible until an order is deleted with its
/// fulfillments still attached. The wallet nests a customer's vouchers by reading exactly these rows
/// (<c>GetUserPurchases</c> takes voucher ids from <c>fulfillments</c>, not from
/// <c>fuel_vouchers.order_id</c>), so an orphaned fulfillment left those vouchers invisible in the app
/// while still sitting in the table.
/// </para>
/// <para>
/// Written by hand precisely because of that: EF sees no diff to script. The configuration now spells
/// the relationship out explicitly (navigation name plus <c>HasForeignKey("OrderId")</c> and the
/// constraint name), which stops EF from scaffolding a second, shadow foreign key column instead of
/// using the existing <c>order_id</c>. Model and database agree again after this runs.
/// </para>
/// <para>
/// The orphan cleanup runs first, otherwise the constraint cannot be added anywhere that has ever had
/// an order deleted without its fulfillments - exactly what the missing constraint permitted.
/// </para>
/// </remarks>
public partial class AddFulfillmentOrderForeignKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM fulfillments f
            WHERE f.order_id IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM orders o WHERE o.id = f.order_id);
            """);

        migrationBuilder.Sql(
            """
            ALTER TABLE fulfillments
            ADD CONSTRAINT "FK_fulfillments_orders_order_id"
            FOREIGN KEY (order_id) REFERENCES orders (id)
            ON DELETE CASCADE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE fulfillments
            DROP CONSTRAINT IF EXISTS "FK_fulfillments_orders_order_id";
            """);
    }
}