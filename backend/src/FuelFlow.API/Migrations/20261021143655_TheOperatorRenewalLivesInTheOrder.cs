using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheOperatorRenewalLivesInTheOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The order carries the money and the brand's invoice for an off-platform renewal
            // (planning #319). An operator's renewal is an order like any other: the line carries
            // the intent and the outcome, the order carries the settlement, the ledger the custody.
            migrationBuilder.AddColumn<DateOnly>(
                name: "external_invoice_date",
                table: "orders",
                type: "date",
                nullable: true,
                comment: "The date on that invoice. NULL with it.");

            migrationBuilder.AddColumn<string>(
                name: "external_invoice_number",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "The brand's invoice for an off-platform renewal - the document the customer paid against. NULL fora self-serve order, where the acquirer's invoice is monobank_invoice_id.");

            // The same renewal to the same date cannot be recorded twice - the folded form of the
            // key the operator's renewals used to carry on their own table.
            migrationBuilder.CreateIndex(
                name: "ix_order_line_items_renewal_once",
                table: "order_line_items",
                columns: new[] { "source_voucher_id", "new_customer_expiration" },
                unique: true,
                filter: "source_voucher_id IS NOT NULL AND new_customer_expiration IS NOT NULL");

            // The contract step of the fold. A drop is irreversible, so the migration refuses to run
            // while a renewal exists that the order lines do not carry - which is exactly the state
            // a database is in if a renewal was confirmed between the shadow write and this deploy.
            // On a database where every renewal is on a line the check finds nothing and the table
            // goes. On production (2026-10-21) it holds zero rows, so this is a no-op there.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM operator_voucher_renewals r
                        WHERE NOT EXISTS (
                            SELECT 1
                            FROM order_line_items l
                            JOIN orders o ON o.id = l.order_id
                            WHERE l.source_voucher_id = r.voucher_id
                              AND l.new_customer_expiration = r.new_expiration
                              AND l.term_code = r.term_code
                              AND l.renewal_branch = r.branch
                              AND o.external_invoice_number IS NOT DISTINCT FROM r.invoice_number
                              AND o.external_invoice_date IS NOT DISTINCT FROM r.invoice_date
                        )
                    ) THEN
                        RAISE EXCEPTION 'operator_voucher_renewals still holds renewals the order lines do not carry: the shadow write has not run for them. Nothing was dropped.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "operator_voucher_renewals");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_order_line_items_renewal_once",
                table: "order_line_items");

            migrationBuilder.DropColumn(
                name: "external_invoice_date",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "external_invoice_number",
                table: "orders");

            migrationBuilder.CreateTable(
                name: "operator_voucher_renewals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    replacement_voucher_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "The fresh stock voucher handed to the customer on the replace branch, or NULL on theextend branch (the same voucher was kept)."),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The customer's voucher that was renewed (extended) or retired (replace)."),
                    acting_user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The admin who recorded the renewal."),
                    acting_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "Their display name at the time, for the audit."),
                    branch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Which branch ran: extend or replace."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "When the renewal was recorded."),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The customer who owns the voucher at the time of the renewal."),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: true, comment: "The brand's накладна / invoice date, when there is one."),
                    invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "The brand's накладна / invoice number, when there is one."),
                    new_expiration = table.Column<DateOnly>(type: "date", nullable: false, comment: "The expiry the customer ends up with - the extended voucher's new expiry, or the stockvoucher's expiry."),
                    old_expiration = table.Column<DateOnly>(type: "date", nullable: false, comment: "The expiry of the customer's voucher before the renewal."),
                    surcharge_uah = table.Column<decimal>(type: "numeric(10,2)", nullable: false, comment: "The доплата the customer paid off-platform; 0 means a free compensation. Audit only - itnever reprices the stock."),
                    term_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, comment: "The chosen term as a stable code (1w/2w/1m...6m)."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operator_voucher_renewals", x => x.id);
                    table.CheckConstraint("ck_operator_renewal_branch", "branch IN ('extend', 'replace')");
                    table.CheckConstraint("ck_operator_renewal_expiration_moves_forward", "new_expiration > old_expiration");
                    table.CheckConstraint("ck_operator_renewal_surcharge_non_negative", "surcharge_uah >= 0");
                    table.ForeignKey(
                        name: "FK_operator_voucher_renewals_fuel_vouchers_replacement_voucher~",
                        column: x => x.replacement_voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_operator_voucher_renewals_fuel_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_operator_voucher_renewals_created_at_utc",
                table: "operator_voucher_renewals",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_operator_voucher_renewals_customer_user_id",
                table: "operator_voucher_renewals",
                column: "customer_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_operator_voucher_renewals_replacement_voucher_id",
                table: "operator_voucher_renewals",
                column: "replacement_voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_operator_voucher_renewals_voucher_id",
                table: "operator_voucher_renewals",
                column: "voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_operator_voucher_renewals_voucher_id_new_expiration",
                table: "operator_voucher_renewals",
                columns: new[] { "voucher_id", "new_expiration" },
                unique: true);
        }
    }
}
