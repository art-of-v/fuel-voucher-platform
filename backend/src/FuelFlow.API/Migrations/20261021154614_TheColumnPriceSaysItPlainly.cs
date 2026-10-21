using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheColumnPriceSaysItPlainly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "pump_price_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "The COLUMN price - what the driver pays at the pump. It is a reference, not a term of our price: our price is the cost plus the margin, and the column price can sit below what we charge. It is the price the customer sees struck through when the column price sits above what we charge.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "The COLUMN price - what the driver pays at the pump. It is a reference, not a term of our price: our price is the cost plus the margin, and it can sit below what we charge. It is the price the customer sees struck through when it sits above what we charge.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "pump_price_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "The COLUMN price - what the driver pays at the pump. It is a reference, not a term of our price: our price is the cost plus the margin, and it can sit below what we charge. It is the price the customer sees struck through when it sits above what we charge.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "The COLUMN price - what the driver pays at the pump. It is a reference, not a term of our price: our price is the cost plus the margin, and the column price can sit below what we charge. It is the price the customer sees struck through when the column price sits above what we charge.");
        }
    }
}
