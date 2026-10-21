using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AnUnsetPriceIsNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The rows that said "not priced yet" with a zero say it with an empty column now.
            //
            // Eleven of the twenty-three fuels carried discount_price = 0.00, which is not a price
            // of zero - it is the absence of one, and the column now says so the way its neighbour
            // (the cost) has always said it. The zero was a placeholder the catalogue import left
            // behind, and a placeholder that reads as a number is the thing this column is for:
            // a price of zero is a price, and a price of zero means the customer takes the fuel
            // for nothing.
            //
            // Nothing reaches a customer at 0 either way - a grade with no cost and no margin
            // computes to no price, and the storefront does not list a package with no price - but
            // the column has to mean one thing.
            migrationBuilder.Sql("""
                UPDATE fuel_types SET discount_price = NULL WHERE discount_price = 0;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_prices_non_negative",
                table: "fuel_types");

            migrationBuilder.AlterColumn<decimal>(
                name: "discount_price",
                table: "fuel_types",
                type: "numeric(12,2)",
                nullable: true,
                comment: "What the customer pays per litre. NULL means it is not set: the grade is in the catalogue and nobody has priced it yet. A price of zero is a price, and a price of zero means the customer takes the fuel for nothing - so the unset state is NULL and never 0.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldComment: "What the customer pays per litre. While the fuel is unpriced it equals the column price, because a fuel with no stated cost has no price of its own.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_prices_non_negative",
                table: "fuel_types",
                sql: "base_price >= 0 AND (discount_price IS NULL OR discount_price > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_prices_non_negative",
                table: "fuel_types");

            // Back the other way: a fuel with no price becomes a fuel priced at zero, which is what
            // the column used to mean by "not priced yet" - and what the old CHECK allowed.
            migrationBuilder.Sql("""
                UPDATE fuel_types SET discount_price = 0 WHERE discount_price IS NULL;
                """);

            migrationBuilder.AlterColumn<decimal>(
                name: "discount_price",
                table: "fuel_types",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m,
                comment: "What the customer pays per litre. While the fuel is unpriced it equals the column price, because a fuel with no stated cost has no price of its own.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true,
                oldComment: "What the customer pays per litre. NULL means it is not set: the grade is in the catalogue and nobody has priced it yet. A price of zero is a price, and a price of zero means the customer takes the fuel for nothing - so the unset state is NULL and never 0.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_prices_non_negative",
                table: "fuel_types",
                sql: "base_price >= 0 AND discount_price >= 0");
        }
    }
}
