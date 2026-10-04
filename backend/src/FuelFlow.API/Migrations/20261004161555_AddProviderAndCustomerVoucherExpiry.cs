using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Splits the single voucher expiry into the two dates the business actually has: the real term
    /// printed on the supplier's document (the ceiling) and the shorter term we sold the customer.
    /// See planning issue #166.
    /// </summary>
    public partial class AddProviderAndCustomerVoucherExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The existing column holds the date read off the supplier's paper at import, so it is
            // the provider term by definition - rename rather than copy, keeping the index.
            migrationBuilder.RenameColumn(
                name: "expiration_date",
                table: "fuel_vouchers",
                newName: "provider_expiration_date");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_expiration_date",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_provider_expiration_date");

            // Added nullable first so the backfill can copy the provider date across. Letting EF emit
            // a NOT NULL column here would stamp every existing row with its default (0001-01-01),
            // which would expire the entire installed base overnight.
            migrationBuilder.AddColumn<DateOnly>(
                name: "customer_expiration_date",
                table: "fuel_vouchers",
                type: "date",
                nullable: true);

            // Every voucher sold so far was sold with its full remaining term - nothing shortens a
            // term yet - so the customer date starts equal to the provider date. When short terms
            // ship, this is the column that diverges.
            migrationBuilder.Sql(
                """
                UPDATE "fuel_vouchers"
                SET "customer_expiration_date" = "provider_expiration_date"
                WHERE "customer_expiration_date" IS NULL;
                """);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "customer_expiration_date",
                table: "fuel_vouchers",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_customer_expiration_date",
                table: "fuel_vouchers",
                column: "customer_expiration_date");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Lossy by nature: the single old column cannot represent two dates, so rolling back keeps
        /// the provider term and discards any shortened customer term. Acceptable only while the
        /// two are equal, which is the case until short terms ship.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_customer_expiration_date",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "customer_expiration_date",
                table: "fuel_vouchers");

            migrationBuilder.RenameColumn(
                name: "provider_expiration_date",
                table: "fuel_vouchers",
                newName: "expiration_date");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_provider_expiration_date",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_expiration_date");
        }
    }
}