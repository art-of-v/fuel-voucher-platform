using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderHistoryAndPricingBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pricing_basis",
                table: "order_line_items",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    external_ref = table.Column<string>(type: "text", nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_events", x => x.id);
                    table.CheckConstraint("ck_order_events_event_type", "event_type IN ('Created', 'StatusChanged', 'InvoiceCreated', 'PaymentCaptured', 'Fulfilled', 'PartiallyFulfilled', 'Refunded', 'Cancelled')");
                    table.CheckConstraint("ck_order_events_from_to", "(event_type = 'Created' AND from_status IS NULL) OR (event_type <> 'Created' AND to_status IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_order_events_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_events_external_ref",
                table: "order_events",
                column: "external_ref");

            migrationBuilder.CreateIndex(
                name: "IX_order_events_occurred_at_utc",
                table: "order_events",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_order_events_order_id_occurred_at_utc",
                table: "order_events",
                columns: new[] { "order_id", "occurred_at_utc" });

            // The two CHECK constraints are emitted inside the CREATE TABLE above, because EF read them from
            // OrderEventConfiguration.ToTable. There is nothing to add by hand here, and adding it
            // would be a duplicate-constraint failure.

            // Backfill one synthetic Created event per existing order, dated from the order's own
            // created_at_utc. That is the entire history of every order that already exists.
            //
            // Leaving the table empty would be the comfortable option and the dishonest one: it
            // would leave no evidence that these orders have no history. One line saying "it started
            // here and nothing since was recorded" is what a reader actually needs.
            migrationBuilder.Sql("""
                INSERT INTO order_events (id, order_id, event_type, from_status, to_status, occurred_at_utc)
                SELECT gen_random_uuid(), o.id, 'Created', NULL, o.status::text, o.created_at_utc
                FROM orders o;
                """);

            // pricing_basis is deliberately left NULL on every pre-existing row. Backfilling it from
            // the live contract or the live fuel configuration would write a history that never
            // happened - contracts change, so today's terms are not the ones that priced a past
            // order. An honest gap can still be reconstructed by asking someone; a fabricated basis
            // cannot be reconstructed at all, because it is indistinguishable from a real one.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_events");

            migrationBuilder.DropColumn(
                name: "pricing_basis",
                table: "order_line_items");
        }
    }
}
