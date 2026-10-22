using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// The two stored copies of the price leave <c>fuel_types</c>, and with them the CHECK that
    /// guarded them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The price is <c>cost + margin</c> (planning <c>docs/PRICING_MODEL.md</c>, 2026-10-21), and
    /// the column price beside it is a reference the customer is shown against - never a term of
    /// our price. The two columns that carried copies of those numbers
    /// (<c>base_price</c>, the list price, and <c>discount_price</c>, the price the customer pays)
    /// had four writers keeping them in step by hand, and they disagreed: the UPG/WOG importer
    /// wrote <c>0.00</c> for a fuel with no cost, the OKKO importer wrote NULL, and fourteen of the
    /// twenty-three fuels on production carried a zero.
    /// </para>
    /// <para>
    /// <b>The CHECK goes with the columns.</b> <c>ck_fuel_types_prices_non_negative</c> named both
    /// of them - <c>base_price >= 0 AND (discount_price IS NULL OR discount_price &gt; 0)</c> -
    /// and PostgreSQL drops a constraint that references a dropped column on its own, so it is
    /// dropped here first and not put back. The rules that survive are the ones that guard the
    /// knobs the price is computed from: <c>ck_fuel_types_pricing_knobs_non_negative</c> (the cost
    /// and the column price are not negative) and <c>ck_fuel_types_margin_signs</c> (the margin is
    /// not negative).
    /// </para>
    /// <para>
    /// <b>Nothing is migrated.</b> The copies are not read by anything: the customer price is
    /// computed from the knobs, and the reference the customer's saving is measured against is the
    /// column price next door. The seed stops writing them for the same reason.
    /// </para>
    /// <para>
    /// <b>The public contract does not change.</b> <c>GET /api/Brands/fuel-types</c> still
    /// publishes <c>basePrice</c> and <c>discountPrice</c> - it always did, and the mobile reads
    /// both - but it computes them: <c>basePrice</c> is the column price and <c>discountPrice</c>
    /// is the price we charge. The names are the app's, and they have meant that to the app all
    /// along.
    /// </para>
    /// </remarks>
    public partial class TheTwoRetiredPriceColumnsGo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_prices_non_negative",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "base_price",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "discount_price",
                table: "fuel_types");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "base_price",
                table: "fuel_types",
                type: "numeric(12,2)",
                nullable: true,
                comment: "The list price of the fuel per litre - the price before our discount. It is the reference the customer's saving is measured against. RETIRED: the reference is the COLUMN price next door, and the saving is computed from it. The column stays until the migration that drops it.");

            migrationBuilder.AddColumn<decimal>(
                name: "discount_price",
                table: "fuel_types",
                type: "numeric(12,2)",
                nullable: true,
                comment: "What the customer pays per litre. NULL means it is not set: the grade is in the catalogue and nobody has priced it yet. A price of zero is a price, and a price of zero means the customer takes the fuel for nothing - so the unset state is NULL and never 0.");

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("0110fe73-be2f-3fcf-2918-0f12dbee1632"),
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 56m, 53m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("054c76e9-40d0-2e6e-4c3c-d0a0d7f7ee93"),
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 55m, 52m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("6cef5b9f-5255-7686-6ac8-bc808a3c4280"),
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 55m, 52m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("7051b2a3-249e-7e2e-3e8a-048e9916b799"),
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 56m, 53m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("74165925-2a26-19d3-3065-50beac6c2aa0"),
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 58m, 55m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("79ce59bb-c52a-1a47-3586-f58ea92a376b"),
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 29m, 27m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("cb73475d-196b-5682-0376-1916b4bc0bb9"),
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 29m, 27m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("cc5d2c86-bebf-f77e-e793-118285578549"),
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 54m, 51m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("cfa77d27-5856-6281-d1af-51fa8c83c913"),
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 65m, 61m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("fa9f1398-0165-d599-8dfb-7880f4d3a479"),
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 60m, 56m });

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_prices_non_negative",
                table: "fuel_types",
                sql: "base_price >= 0 AND (discount_price IS NULL OR discount_price > 0)");
        }
    }
}
