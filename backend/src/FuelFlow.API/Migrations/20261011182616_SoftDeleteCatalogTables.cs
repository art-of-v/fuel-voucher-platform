using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteCatalogTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "stations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "stations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "station_nodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "station_nodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "fuel_types",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "fuel_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "fuel_packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "fuel_packages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-10",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-20",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-50",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-10",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-20",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-50",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-10",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-20",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-50",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-95",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-dp",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-gas",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-p95",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-pulls-dp",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-100",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-95",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-95-euro",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-dp",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-gas",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "klo",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "okko",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "upg",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "wog",
                columns: new[] { "deleted_at_utc", "is_deleted" },
                values: new object[] { null, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "stations");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "stations");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "station_nodes");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "station_nodes");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "fuel_packages");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "fuel_packages");
        }
    }
}
