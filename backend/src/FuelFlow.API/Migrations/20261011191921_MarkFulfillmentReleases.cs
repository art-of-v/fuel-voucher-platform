using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class MarkFulfillmentReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fulfillments_order_id",
                table: "fulfillments");

            migrationBuilder.AddColumn<string>(
                name: "release_reason",
                table: "fulfillments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "released_at_utc",
                table: "fulfillments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fulfillments_order_id",
                table: "fulfillments",
                column: "order_id",
                filter: "released_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_fulfillments_released_at_utc",
                table: "fulfillments",
                column: "released_at_utc",
                filter: "released_at_utc IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fulfillments_order_id",
                table: "fulfillments");

            migrationBuilder.DropIndex(
                name: "IX_fulfillments_released_at_utc",
                table: "fulfillments");

            migrationBuilder.DropColumn(
                name: "release_reason",
                table: "fulfillments");

            migrationBuilder.DropColumn(
                name: "released_at_utc",
                table: "fulfillments");

            migrationBuilder.CreateIndex(
                name: "IX_fulfillments_order_id",
                table: "fulfillments",
                column: "order_id");
        }
    }
}
