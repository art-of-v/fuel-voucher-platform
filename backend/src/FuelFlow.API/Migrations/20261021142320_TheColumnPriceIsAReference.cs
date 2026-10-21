using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheColumnPriceIsAReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
                oldComment: "The COLUMN price - what the driver pays at the pump. It is the ceiling: our price is never above it, and it is the price the customer sees struck through when our price is below it.");

            migrationBuilder.AlterColumn<decimal>(
                name: "min_discount_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "The smallest discount we give the customer per litre. RETIRED: the price is computed as cost + margin, and the column price beside it is a reference, so this column no longer takes part in it. It stays until the migration that drops it.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "The smallest discount we give the customer per litre, so that our price always sits below the column price - we are never dearer than the pump. RETIRED: the price is computed as min(cost + margin, pump), and this column no longer takes part in it. It stays until the migration that drops it.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "pump_price_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "The COLUMN price - what the driver pays at the pump. It is the ceiling: our price is never above it, and it is the price the customer sees struck through when our price is below it.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "The COLUMN price - what the driver pays at the pump. It is a reference, not a term of our price: our price is the cost plus the margin, and it can sit below what we charge. It is the price the customer sees struck through when it sits above what we charge.");

            migrationBuilder.AlterColumn<decimal>(
                name: "min_discount_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "The smallest discount we give the customer per litre, so that our price always sits below the column price - we are never dearer than the pump. RETIRED: the price is computed as min(cost + margin, pump), and this column no longer takes part in it. It stays until the migration that drops it.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "The smallest discount we give the customer per litre. RETIRED: the price is computed as cost + margin, and the column price beside it is a reference, so this column no longer takes part in it. It stays until the migration that drops it.");
        }
    }
}
