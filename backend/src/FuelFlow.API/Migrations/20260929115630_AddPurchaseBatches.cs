using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fuel_type_id = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    cost_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: false),
                    entered_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_batches", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_batches_fuel_type_id",
                table: "purchase_batches",
                column: "fuel_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_batches_import_job_id_fuel_type_id",
                table: "purchase_batches",
                columns: new[] { "import_job_id", "fuel_type_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_batches");
        }
    }
}
