using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeSettingsAndPartialFulfillmentSince : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "partially_fulfilled_since_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_settings", x => x.key);
                });

            migrationBuilder.InsertData(
                table: "app_settings",
                columns: new[] { "key", "value", "updated_at_utc" },
                values: new object[,]
                {
                    { "AutoRefund:Enabled", "false", new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc) },
                    { "AutoRefund:DelayDays", "7", new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc) }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropColumn(
                name: "partially_fulfilled_since_utc",
                table: "orders");
        }
    }
}
