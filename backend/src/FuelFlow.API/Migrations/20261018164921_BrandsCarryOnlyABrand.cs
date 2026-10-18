using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class BrandsCarryOnlyABrand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_brands_station_type",
                table: "brands");

            migrationBuilder.DropColumn(
                name: "address",
                table: "brands");

            migrationBuilder.DropColumn(
                name: "lat",
                table: "brands");

            migrationBuilder.DropColumn(
                name: "lng",
                table: "brands");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "brands");

            migrationBuilder.DropColumn(
                name: "station_type",
                table: "brands");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "brands",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "lat",
                table: "brands",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "lng",
                table: "brands",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "brands",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "station_type",
                table: "brands",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "brands",
                keyColumn: "id",
                keyValue: "klo",
                columns: new[] { "address", "lat", "lng", "phone", "station_type" },
                values: new object[] { null, 50.4101, 30.403400000000001, null, null });

            migrationBuilder.UpdateData(
                table: "brands",
                keyColumn: "id",
                keyValue: "okko",
                columns: new[] { "address", "lat", "lng", "phone", "station_type" },
                values: new object[] { null, 50.485100000000003, 30.473400000000002, null, null });

            migrationBuilder.UpdateData(
                table: "brands",
                keyColumn: "id",
                keyValue: "upg",
                columns: new[] { "address", "lat", "lng", "phone", "station_type" },
                values: new object[] { null, 50.400100000000002, 30.613399999999999, null, null });

            migrationBuilder.UpdateData(
                table: "brands",
                keyColumn: "id",
                keyValue: "wog",
                columns: new[] { "address", "lat", "lng", "phone", "station_type" },
                values: new object[] { null, 50.450099999999999, 30.523399999999999, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_brands_station_type",
                table: "brands",
                column: "station_type");
        }
    }
}
