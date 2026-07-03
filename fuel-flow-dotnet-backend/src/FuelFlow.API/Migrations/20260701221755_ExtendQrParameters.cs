using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class ExtendQrParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_qr_parameters_ecc_level_version",
                table: "qr_parameters");

            migrationBuilder.AddColumn<string>(
                name: "encoding_mode",
                table: "qr_parameters",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "mask_pattern",
                table: "qr_parameters",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_qr_parameters_ecc_level_version_mask_pattern_encoding_mode",
                table: "qr_parameters",
                columns: new[] { "ecc_level", "version", "mask_pattern", "encoding_mode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_qr_parameters_ecc_level_version_mask_pattern_encoding_mode",
                table: "qr_parameters");

            migrationBuilder.DropColumn(
                name: "encoding_mode",
                table: "qr_parameters");

            migrationBuilder.DropColumn(
                name: "mask_pattern",
                table: "qr_parameters");

            migrationBuilder.CreateIndex(
                name: "IX_qr_parameters_ecc_level_version",
                table: "qr_parameters",
                columns: new[] { "ecc_level", "version" },
                unique: true);
        }
    }
}
