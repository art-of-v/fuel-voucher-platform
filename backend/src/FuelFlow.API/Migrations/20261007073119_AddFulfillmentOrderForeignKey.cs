using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations;

/// <summary>
/// Binds a fulfillment to its order in the database.
/// </summary>
/// <remarks>
/// <para>
/// <c>fulfillments.order_id</c> shipped as a bare column with an index and no foreign key, so
/// deleting an order left its fulfillments pointing at nothing. The wallet nests vouchers by
/// reading exactly these rows (<c>GetUserPurchases</c> reads voucher ids from
/// <c>fulfillments</c>, not from <c>fuel_vouchers.order_id</c>), so an orphaned fulfillment left
/// the vouchers a customer holds invisible in the app while they still existed in the table.
/// </para>
/// <para>
/// Added as explicit SQL rather than through the model on purpose: EF's scaffolding for this
/// relationship invents a second, shadow foreign key column instead of using the existing
/// <c>order_id</c>, and no fulfillment foreign key has ever been represented in the migrations
/// (not even the configured <c>voucher_id</c> one). The database is the right place for this
/// integrity rule, so it is written here rather than forced into the model. <c>Down</c> removes it.
/// </para>
/// <para>
/// The orphan cleanup runs first, otherwise the constraint cannot be added on any environment that
/// has ever had an order deleted without its fulfillments - which is precisely what the missing
/// constraint allowed.
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