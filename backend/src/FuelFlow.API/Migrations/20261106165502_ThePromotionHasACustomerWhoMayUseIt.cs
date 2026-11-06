using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class ThePromotionHasACustomerWhoMayUseIt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "promo_access_granted_at_utc",
                table: "users",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When this customer was given access to the promotion, or NULL for none. The instant IS the flag: a boolean beside a granted-at would be two columns claiming to know one fact, and they can disagree. Only staff of Manager rank or above may write it, because it opens a way to be served without the acquirer ever seeing the sale.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "promo_access_granted_at_utc",
                table: "users");
        }
    }
}
