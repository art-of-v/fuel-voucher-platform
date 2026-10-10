using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierRequisites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "station_id",
                table: "suppliers");

            migrationBuilder.RenameColumn(
                name: "contact_info",
                table: "suppliers",
                newName: "address");

            migrationBuilder.AddColumn<string>(
                name: "edr_ipn",
                table: "suppliers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "suppliers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "legal_form",
                table: "suppliers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "suppliers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "suppliers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rnkrr",
                table: "suppliers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "edr_ipn",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "email",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "legal_form",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "rnkrr",
                table: "suppliers");

            migrationBuilder.RenameColumn(
                name: "address",
                table: "suppliers",
                newName: "contact_info");

            migrationBuilder.AddColumn<string>(
                name: "station_id",
                table: "suppliers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}
