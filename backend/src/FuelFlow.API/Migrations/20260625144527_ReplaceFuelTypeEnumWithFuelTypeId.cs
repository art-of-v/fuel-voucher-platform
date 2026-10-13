using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceFuelTypeEnumWithFuelTypeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_fuel_type",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_provider_fuel_type_liters_status",
                table: "fuel_vouchers");

            // Add new columns first
            migrationBuilder.AddColumn<string>(
                name: "fuel_type_id",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fuel_type_id",
                table: "fuel_vouchers",
                type: "text",
                maxLength: 100,
                nullable: true);

            // Migrate data for vouchers: map old enum to provider-specific fuel type IDs
            migrationBuilder.Sql(@"
                UPDATE fuel_vouchers
                SET fuel_type_id = CASE
                    WHEN provider = 'WOG' AND fuel_type = 'Diesel' THEN 'wog-dp'
                    WHEN provider = 'WOG' AND fuel_type = 'Gasoline95' THEN 'wog-95'
                    WHEN provider = 'WOG' AND fuel_type = 'Gasoline98' THEN 'wog-100'
                    WHEN provider = 'WOG' AND fuel_type = 'LPG' THEN 'wog-gas'
                    WHEN provider = 'OKKO' AND fuel_type = 'Diesel' THEN 'okko-dp'
                    WHEN provider = 'OKKO' AND fuel_type = 'Gasoline95' THEN 'okko-95'
                    WHEN provider = 'OKKO' AND fuel_type = 'Gasoline98' THEN 'okko-p95'
                    WHEN provider = 'OKKO' AND fuel_type = 'LPG' THEN 'okko-gas'
                    ELSE 'okko-dp'
                END
            ");

            // Migrate data for orders
            migrationBuilder.Sql(@"
                UPDATE orders
                SET fuel_type_id = CASE
                    WHEN provider = 'WOG' AND fuel_type = 'Diesel' THEN 'wog-dp'
                    WHEN provider = 'WOG' AND fuel_type = 'Gasoline95' THEN 'wog-95'
                    WHEN provider = 'WOG' AND fuel_type = 'Gasoline98' THEN 'wog-100'
                    WHEN provider = 'WOG' AND fuel_type = 'LPG' THEN 'wog-gas'
                    WHEN provider = 'OKKO' AND fuel_type = 'Diesel' THEN 'okko-dp'
                    WHEN provider = 'OKKO' AND fuel_type = 'Gasoline95' THEN 'okko-95'
                    WHEN provider = 'OKKO' AND fuel_type = 'Gasoline98' THEN 'okko-p95'
                    WHEN provider = 'OKKO' AND fuel_type = 'LPG' THEN 'okko-gas'
                    ELSE 'okko-dp'
                END
            ");

            // Now make columns non-nullable and drop old columns
            migrationBuilder.AlterColumn<string>(
                name: "fuel_type_id",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "fuel_type_id",
                table: "fuel_vouchers",
                type: "text",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "fuel_type",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "fuel_type",
                table: "fuel_vouchers");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_fuel_type_id",
                table: "fuel_vouchers",
                column: "fuel_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_provider_fuel_type_id_liters_status",
                table: "fuel_vouchers",
                columns: new[] { "provider", "fuel_type_id", "liters", "status" });

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_fuel_types_fuel_type_id",
                table: "fuel_vouchers",
                column: "fuel_type_id",
                principalTable: "fuel_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fuel_vouchers_fuel_types_fuel_type_id",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_fuel_type_id",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_provider_fuel_type_id_liters_status",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "fuel_type_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "fuel_type_id",
                table: "fuel_vouchers");

            migrationBuilder.AddColumn<string>(
                name: "fuel_type",
                table: "orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "fuel_type",
                table: "fuel_vouchers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_fuel_type",
                table: "fuel_vouchers",
                column: "fuel_type");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_provider_fuel_type_liters_status",
                table: "fuel_vouchers",
                columns: new[] { "provider", "fuel_type", "liters", "status" });
        }
    }
}
