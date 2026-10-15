using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class UniqueMonobankInvoiceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-added, and re-scaffolding drops it.
            //
            // A unique index cannot be created over data that already breaks it, and Postgres reports
            // that as a bare "duplicate key value violates unique constraint" naming neither the
            // table nor the value. With the DDL ahead of it, this guard would never get to speak and
            // the operator would be left with a mystery message - so it runs FIRST and raises with
            // the offending values.
            //
            // It deliberately does not decide which order to keep. Two orders claiming one invoice
            // is a real reconciliation question, and the answer is a person's, not a migration's:
            // which one did Monobank actually charge, and does the other need a void? order_events
            // now records the history that makes the question answerable.
            //
            // Measured on production 2026-10-15, before this migration: 0 orders, 0 duplicates.
            migrationBuilder.Sql("""
                DO $$
                DECLARE dupes text;
                BEGIN
                    SELECT string_agg(monobank_invoice_id, ', ' ORDER BY monobank_invoice_id)
                      INTO dupes
                      FROM (
                          SELECT monobank_invoice_id
                          FROM orders
                          WHERE monobank_invoice_id IS NOT NULL
                          GROUP BY monobank_invoice_id
                          HAVING count(*) > 1
                      ) d;

                    IF dupes IS NOT NULL THEN
                        RAISE EXCEPTION
                            'orders: % invoice id(s) are claimed by more than one order (%). Each invoice is what Monobank charged against, so this needs resolving by hand - order_events records the history that shows which order it belongs to.',
                            (SELECT count(*) FROM (
                                SELECT monobank_invoice_id FROM orders WHERE monobank_invoice_id IS NOT NULL
                                GROUP BY monobank_invoice_id HAVING count(*) > 1) x),
                            dupes;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_orders_monobank_invoice_id",
                table: "orders",
                column: "monobank_invoice_id",
                unique: true,
                filter: "monobank_invoice_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_monobank_invoice_id",
                table: "orders");
        }
    }
}
