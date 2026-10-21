using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// The price is computed, not copied: the fuel keeps four numbers and every price is derived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>fuel_types</c> carried three numbers about a price that one formula computes:
    /// <c>base_price</c> (the reference the customer's saving is measured against), <c>discount_price</c>
    /// (what the customer pays) and <c>min_discount_per_liter</c> (a dial under the column price).
    /// Four writers maintained them — the three price importers, the catalogue import, the operator's
    /// panel and the batch-cost recalculator — and they disagreed:
    /// </para>
    /// <list type="bullet">
    /// <item><description>the UPG/WOG importer wrote <c>0.00</c> for a fuel with no cost, because it
    /// took the non-nullable formula with the cost defaulted to zero;</description></item>
    /// <item><description>the OKKO importer wrote NULL;</description></item>
    /// <item><description>the KLO importer wrote the column price;</description></item>
    /// <item><description>the admin card seeded the minimum-discount knob from a DISPLAY number, so
    /// saving a fuel whose stored price was zero wrote "at least the whole column price under the
    /// column" into it — and the formula then produced zero, which the below-cost guard refuses.</description></item>
    /// </list>
    /// <para>
    /// This migration does not drop the three columns: it makes <c>base_price</c> nullable, so the
    /// code can stop writing it, and it records on each column that it is retired. The columns go in
    /// the next migration, which is the one that changes the model as well - the model and the
    /// migrations cannot be separated, because the build gate refuses a model change with no
    /// migration.
    /// </para>
    /// <para>
    /// <b>No data is touched.</b> The copies keep the values they had; nothing reads them, and the
    /// pending migration <c>20261021073230_AnUnsetPriceIsNull</c> has already turned the fourteen
    /// zeroes into NULLs. The customer price is <c>min(cost + margin, pump)</c> from now on, and the
    /// reference is the column price next door.
    /// </para>
    /// </remarks>
    public partial class ThePriceIsComputedNotCopied : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "min_discount_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "The smallest discount we give the customer per litre, so that our price always sits below the column price - we are never dearer than the pump. RETIRED: the price is computed as min(cost + margin, pump), and this column no longer takes part in it. It stays until the migration that drops it.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "The smallest discount we give the customer per litre, so that our price always sits below the column price - we are never dearer than the pump.");

            migrationBuilder.AlterColumn<decimal>(
                name: "base_price",
                table: "fuel_types",
                type: "numeric(12,2)",
                nullable: true,
                comment: "The list price of the fuel per litre - the price before our discount. It is the reference the customer's saving is measured against. RETIRED: the reference is the COLUMN price next door, and the saving is computed from it. The column stays until the migration that drops it.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldComment: "The list price of the fuel per litre - the price before our discount. It is the reference the customer's saving is measured against.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "min_discount_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "The smallest discount we give the customer per litre, so that our price always sits below the column price - we are never dearer than the pump.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "The smallest discount we give the customer per litre, so that our price always sits below the column price - we are never dearer than the pump. RETIRED: the price is computed as min(cost + margin, pump), and this column no longer takes part in it. It stays until the migration that drops it.");

            migrationBuilder.AlterColumn<decimal>(
                name: "base_price",
                table: "fuel_types",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m,
                comment: "The list price of the fuel per litre - the price before our discount. It is the reference the customer's saving is measured against.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true,
                oldComment: "The list price of the fuel per litre - the price before our discount. It is the reference the customer's saving is measured against. RETIRED: the reference is the COLUMN price next door, and the saving is computed from it. The column stays until the migration that drops it.");
        }
    }
}
