using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSeedStationNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "station_nodes",
                keyColumn: "id",
                keyValue: "klo-kyiv-main");

            migrationBuilder.DeleteData(
                table: "station_nodes",
                keyColumn: "id",
                keyValue: "okko-kyiv-main");

            migrationBuilder.DeleteData(
                table: "station_nodes",
                keyColumn: "id",
                keyValue: "upg-kyiv-main");

            migrationBuilder.DeleteData(
                table: "station_nodes",
                keyColumn: "id",
                keyValue: "wog-kyiv-main");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "station_nodes",
                columns: new[] { "id", "address", "city", "created_at_utc", "lat", "lng", "name", "phone", "station_id", "station_type", "updated_at_utc" },
                values: new object[,]
                {
                    { "klo-kyiv-main", "Броварський проспект, 11", "Київ", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 50.457799999999999, 30.598600000000001, "KLO Київ", null, "klo", "Тип АЗС KLO-міська", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-kyiv-main", "42 Чоколівський бульвар", "Київ", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 50.430999999999997, 30.451499999999999, "OKKO Київ", null, "okko", "Тип АЗС ОККО-міська", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "upg-kyiv-main", "Проспект Перемоги, 98", "Київ", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 50.456600000000002, 30.395, "UPG Київ", null, "upg", "Тип АЗС UPG-міська", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-kyiv-main", "15-Б проспект Соборності", "Київ", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 50.4482, 30.617000000000001, "WOG Київ", null, "wog", "Тип АЗС WOG-міська", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });
        }
    }
}
