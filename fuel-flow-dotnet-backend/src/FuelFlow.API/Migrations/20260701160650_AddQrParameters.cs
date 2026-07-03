using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddQrParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "qr_parameters_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "qr_parameters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: true),
                    ecc_level = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qr_parameters", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_qr_parameters_id",
                table: "fuel_vouchers",
                column: "qr_parameters_id");

            migrationBuilder.CreateIndex(
                name: "IX_qr_parameters_ecc_level_version",
                table: "qr_parameters",
                columns: new[] { "ecc_level", "version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_qr_parameters_qr_parameters_id",
                table: "fuel_vouchers",
                column: "qr_parameters_id",
                principalTable: "qr_parameters",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fuel_vouchers_qr_parameters_qr_parameters_id",
                table: "fuel_vouchers");

            migrationBuilder.DropTable(
                name: "qr_parameters");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_qr_parameters_id",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "qr_parameters_id",
                table: "fuel_vouchers");
        }
    }
}
