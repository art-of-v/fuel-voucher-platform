using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class NoOneOffPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offer_volumes_price_override_non_negative",
                table: "fuel_offer_volumes");

            migrationBuilder.DropColumn(
                name: "price_override_uah",
                table: "fuel_offer_volumes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "price_override_uah",
                table: "fuel_offer_volumes",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-95-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-95-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-95-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-100-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-100-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-100-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offer_volumes_price_override_non_negative",
                table: "fuel_offer_volumes",
                sql: "price_override_uah IS NULL OR price_override_uah >= 0");
        }
    }
}
