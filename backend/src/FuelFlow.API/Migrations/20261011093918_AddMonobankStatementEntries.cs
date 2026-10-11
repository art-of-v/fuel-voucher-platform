using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddMonobankStatementEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monobank_statement_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant = table.Column<int>(type: "integer", nullable: false),
                    statement_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    invoice_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    amount_kopecks = table.Column<long>(type: "bigint", nullable: true),
                    ccy = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    acquirer_created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    acquirer_modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ext_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    masked_card = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    rrn = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    approval_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    raw_payload = table.Column<string>(type: "text", nullable: true),
                    first_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monobank_statement_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_monobank_statement_entries_invoice_id",
                table: "monobank_statement_entries",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_monobank_statement_entries_merchant_acquirer_created_at_utc",
                table: "monobank_statement_entries",
                columns: new[] { "merchant", "acquirer_created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_monobank_statement_entries_merchant_statement_id",
                table: "monobank_statement_entries",
                columns: new[] { "merchant", "statement_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_monobank_statement_entries_reference",
                table: "monobank_statement_entries",
                column: "reference");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monobank_statement_entries");
        }
    }
}
