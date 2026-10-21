using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheSchemaDocumentsItself : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "supplier_price_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "What WE pay the network per litre. NULL means the cost is not stated: the fuel is unpriced, and the below-cost guard has nothing to compare against.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "pump_price_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "The COLUMN price - what the driver pays at the pump. It is the ceiling: our price is never above it, and it is the price the customer sees struck through when our price is below it.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "min_discount_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "The smallest discount we give the customer per litre, so that our price always sits below the column price - we are never dearer than the pump.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "margin_uah_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "What we add per litre on top of what we pay. NULL means the margin is not stated, and a fuel with no stated cost OR no stated margin has no price of its own: it is shown at the column price, marked 'not in stock', and it cannot be sold.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "margin_percent",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "The margin as a percentage, recorded for the operator's card. The price is computed from the ABSOLUTE margin (margin_uah_per_liter); this column does not feed the formula.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "discount_price",
                table: "fuel_types",
                type: "numeric(12,2)",
                nullable: false,
                comment: "What the customer pays per litre. While the fuel is unpriced it equals the column price, because a fuel with no stated cost has no price of its own.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "base_price",
                table: "fuel_types",
                type: "numeric(12,2)",
                nullable: false,
                comment: "The list price of the fuel per litre - the price before our discount. It is the reference the customer's saving is measured against.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.AlterColumn<bool>(
                name: "allow_below_cost",
                table: "fuel_types",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether this fuel may be sold below what we pay. A manager's opt-in, per fuel, for a deliberate loss leader - the write path refuses such a sale without it.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "supplier_price_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "What WE pay the network per litre. NULL means the cost is not stated: the fuel is unpriced, and the below-cost guard has nothing to compare against.");

            migrationBuilder.AlterColumn<decimal>(
                name: "pump_price_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "The COLUMN price - what the driver pays at the pump. It is the ceiling: our price is never above it, and it is the price the customer sees struck through when our price is below it.");

            migrationBuilder.AlterColumn<decimal>(
                name: "min_discount_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "The smallest discount we give the customer per litre, so that our price always sits below the column price - we are never dearer than the pump.");

            migrationBuilder.AlterColumn<decimal>(
                name: "margin_uah_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "What we add per litre on top of what we pay. NULL means the margin is not stated, and a fuel with no stated cost OR no stated margin has no price of its own: it is shown at the column price, marked 'not in stock', and it cannot be sold.");

            migrationBuilder.AlterColumn<decimal>(
                name: "margin_percent",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "The margin as a percentage, recorded for the operator's card. The price is computed from the ABSOLUTE margin (margin_uah_per_liter); this column does not feed the formula.");

            migrationBuilder.AlterColumn<decimal>(
                name: "discount_price",
                table: "fuel_types",
                type: "numeric(12,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldComment: "What the customer pays per litre. While the fuel is unpriced it equals the column price, because a fuel with no stated cost has no price of its own.");

            migrationBuilder.AlterColumn<decimal>(
                name: "base_price",
                table: "fuel_types",
                type: "numeric(12,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldComment: "The list price of the fuel per litre - the price before our discount. It is the reference the customer's saving is measured against.");

            migrationBuilder.AlterColumn<bool>(
                name: "allow_below_cost",
                table: "fuel_types",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false,
                oldComment: "Whether this fuel may be sold below what we pay. A manager's opt-in, per fuel, for a deliberate loss leader - the write path refuses such a sale without it.");
        }
    }
}
