using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class FixTypeMismatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchases");

            migrationBuilder.DropTable(
                name: "vouchers");

            migrationBuilder.AlterColumn<double>(
                name: "lng",
                table: "stations",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "lat",
                table: "stations",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at_utc",
                table: "stations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<double>(
                name: "lng",
                table: "station_nodes",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "lat",
                table: "station_nodes",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at_utc",
                table: "station_nodes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "orders",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<decimal>(
                name: "liters",
                table: "orders",
                type: "numeric(10,2)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at_utc",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<Guid>(
                name: "assigned_to_user_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "fuel_vouchers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at_utc",
                table: "fuel_types",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<decimal>(
                name: "liters",
                table: "fuel_packages",
                type: "numeric(10,2)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at_utc",
                table: "fuel_packages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-10",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 10m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-20",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 20m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-50",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 50m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 10m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 2m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 20m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 3m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 50m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 10m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 20m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 50m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 10m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 20m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 50m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 10m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 20m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 50m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-10",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 10m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-20",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 20m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-50",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 50m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-10",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 10m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-20",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 20m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-50",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 50m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 10m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 20m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 50m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 10m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 20m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 50m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 10m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 20m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                columns: new[] { "liters", "updated_at_utc" },
                values: new object[] { 50m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-95",
                column: "updated_at_utc",
                value: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-dp",
                column: "updated_at_utc",
                value: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-gas",
                column: "updated_at_utc",
                value: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-p95",
                column: "updated_at_utc",
                value: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-pulls-dp",
                column: "updated_at_utc",
                value: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-100",
                column: "updated_at_utc",
                value: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-95",
                column: "updated_at_utc",
                value: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-95-euro",
                column: "updated_at_utc",
                value: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-dp",
                column: "updated_at_utc",
                value: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-gas",
                column: "updated_at_utc",
                value: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "station_nodes",
                keyColumn: "id",
                keyValue: "klo-kyiv-main",
                columns: new[] { "lat", "lng", "updated_at_utc" },
                values: new object[] { 50.457799999999999, 30.598600000000001, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "station_nodes",
                keyColumn: "id",
                keyValue: "okko-kyiv-main",
                columns: new[] { "lat", "lng", "updated_at_utc" },
                values: new object[] { 50.430999999999997, 30.451499999999999, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "station_nodes",
                keyColumn: "id",
                keyValue: "upg-kyiv-main",
                columns: new[] { "lat", "lng", "updated_at_utc" },
                values: new object[] { 50.456600000000002, 30.395, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "station_nodes",
                keyColumn: "id",
                keyValue: "wog-kyiv-main",
                columns: new[] { "lat", "lng", "updated_at_utc" },
                values: new object[] { 50.4482, 30.617000000000001, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "klo",
                columns: new[] { "lat", "lng", "updated_at_utc" },
                values: new object[] { 50.4101, 30.403400000000001, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "okko",
                columns: new[] { "lat", "lng", "updated_at_utc" },
                values: new object[] { 50.485100000000003, 30.473400000000002, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "upg",
                columns: new[] { "lat", "lng", "updated_at_utc" },
                values: new object[] { 50.400100000000002, 30.613399999999999, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "wog",
                columns: new[] { "lat", "lng", "updated_at_utc" },
                values: new object[] { 50.450099999999999, 30.523399999999999, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "IX_verification_codes_phone_number_is_used_expires_at_utc",
                table: "verification_codes",
                columns: new[] { "phone_number", "is_used", "expires_at_utc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_user_id_is_revoked",
                table: "refresh_tokens",
                columns: new[] { "user_id", "is_revoked" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_user_id_created_at_utc",
                table: "orders",
                columns: new[] { "user_id", "created_at_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_orders_user_id_status",
                table: "orders",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_assigned_to_user_id_status",
                table: "fuel_vouchers",
                columns: new[] { "assigned_to_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_external_id",
                table: "fuel_vouchers",
                column: "external_id",
                unique: true,
                filter: "external_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_verification_codes_phone_number_is_used_expires_at_utc",
                table: "verification_codes");

            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_user_id_is_revoked",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "IX_orders_user_id_created_at_utc",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_user_id_status",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_assigned_to_user_id_status",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_external_id",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "stations");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "station_nodes");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "fuel_packages");

            migrationBuilder.AlterColumn<string>(
                name: "lng",
                table: "stations",
                type: "text",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "lat",
                table: "stations",
                type: "text",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "lng",
                table: "station_nodes",
                type: "text",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "lat",
                table: "station_nodes",
                type: "text",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "user_id",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "liters",
                table: "orders",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)");

            migrationBuilder.AlterColumn<string>(
                name: "assigned_to_user_id",
                table: "fuel_vouchers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "liters",
                table: "fuel_packages",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)");

            migrationBuilder.CreateTable(
                name: "purchases",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchases", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchases_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vouchers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    fuel_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vouchers", x => x.id);
                });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-10",
                column: "liters",
                value: 10);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-20",
                column: "liters",
                value: 20);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-50",
                column: "liters",
                value: 50);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                column: "liters",
                value: 10);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                column: "liters",
                value: 2);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                column: "liters",
                value: 20);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                column: "liters",
                value: 3);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                column: "liters",
                value: 50);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                column: "liters",
                value: 10);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                column: "liters",
                value: 20);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                column: "liters",
                value: 50);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                column: "liters",
                value: 10);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                column: "liters",
                value: 20);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                column: "liters",
                value: 50);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                column: "liters",
                value: 10);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                column: "liters",
                value: 20);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                column: "liters",
                value: 50);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-10",
                column: "liters",
                value: 10);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-20",
                column: "liters",
                value: 20);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-50",
                column: "liters",
                value: 50);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-10",
                column: "liters",
                value: 10);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-20",
                column: "liters",
                value: 20);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-50",
                column: "liters",
                value: 50);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                column: "liters",
                value: 10);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                column: "liters",
                value: 20);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                column: "liters",
                value: 50);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                column: "liters",
                value: 10);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                column: "liters",
                value: 20);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                column: "liters",
                value: 50);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                column: "liters",
                value: 10);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                column: "liters",
                value: 20);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                column: "liters",
                value: 50);

            migrationBuilder.UpdateData(
                table: "station_nodes",
                keyColumn: "id",
                keyValue: "klo-kyiv-main",
                columns: new[] { "lat", "lng" },
                values: new object[] { "50.4578", "30.5986" });

            migrationBuilder.UpdateData(
                table: "station_nodes",
                keyColumn: "id",
                keyValue: "okko-kyiv-main",
                columns: new[] { "lat", "lng" },
                values: new object[] { "50.4310", "30.4515" });

            migrationBuilder.UpdateData(
                table: "station_nodes",
                keyColumn: "id",
                keyValue: "upg-kyiv-main",
                columns: new[] { "lat", "lng" },
                values: new object[] { "50.4566", "30.3950" });

            migrationBuilder.UpdateData(
                table: "station_nodes",
                keyColumn: "id",
                keyValue: "wog-kyiv-main",
                columns: new[] { "lat", "lng" },
                values: new object[] { "50.4482", "30.6170" });

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "klo",
                columns: new[] { "lat", "lng" },
                values: new object[] { "50.4101", "30.4034" });

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "okko",
                columns: new[] { "lat", "lng" },
                values: new object[] { "50.4851", "30.4734" });

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "upg",
                columns: new[] { "lat", "lng" },
                values: new object[] { "50.4001", "30.6134" });

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "wog",
                columns: new[] { "lat", "lng" },
                values: new object[] { "50.4501", "30.5234" });

            migrationBuilder.CreateIndex(
                name: "IX_purchases_user_id",
                table: "purchases",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_vouchers_external_id",
                table: "vouchers",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vouchers_status",
                table: "vouchers",
                column: "status");
        }
    }
}
