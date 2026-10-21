using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheMinimumDiscountColumnGoes : Migration
    {
        /// <summary>
        /// The minimum-discount knob goes, and with it the last piece of the old price formula.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The price is <c>cost + margin</c> (planning <c>docs/PRICING_MODEL.md</c>, 2026-10-21), and
        /// the column price beside it is a reference the customer is shown against - never a term of
        /// our price. The knob that used to sit under the column (<c>min(cost + margin, pump - min
        /// discount)</c>) was a second dial on one number, four writers kept copies of it, and one of
        /// them - the admin card - fed a displayed saving back into it as a stored setting.
        /// </para>
        /// <para>
        /// The CHECK constraint that names the column goes first: PostgreSQL drops a constraint that
        /// references a dropped column on its own, and this one has to SURVIVE - it still guards the
        /// cost and the column price. So it is dropped, the column goes, and it comes back without
        /// the clause that named the knob.
        /// </para>
        /// <para>
        /// Nothing is backfilled and nothing is migrated: the knob took part in no price anybody
        /// paid after 2026-10-21, and the rows that still carry a value carry zero.
        /// </para>
        /// <para>
        /// <c>fuel_price_history</c> keeps its copy of the column - a history is a record of what was
        /// true at the time, and the knob was true then. New rows leave it NULL.
        /// </para>
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_pricing_knobs_non_negative",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "min_discount_per_liter",
                table: "fuel_types");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_pricing_knobs_non_negative",
                table: "fuel_types",
                sql: "(supplier_price_per_liter IS NULL OR supplier_price_per_liter >= 0) AND (pump_price_per_liter IS NULL OR pump_price_per_liter >= 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_pricing_knobs_non_negative",
                table: "fuel_types");

            migrationBuilder.AddColumn<decimal>(
                name: "min_discount_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "The smallest discount we give the customer per litre. RETIRED: the price is computed as cost + margin, and the column price beside it is a reference, so this column no longer takes part in it. It stays until the migration that drops it.");

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("0110fe73-be2f-3fcf-2918-0f12dbee1632"),
                column: "min_discount_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("054c76e9-40d0-2e6e-4c3c-d0a0d7f7ee93"),
                column: "min_discount_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("6cef5b9f-5255-7686-6ac8-bc808a3c4280"),
                column: "min_discount_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("7051b2a3-249e-7e2e-3e8a-048e9916b799"),
                column: "min_discount_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("74165925-2a26-19d3-3065-50beac6c2aa0"),
                column: "min_discount_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("79ce59bb-c52a-1a47-3586-f58ea92a376b"),
                column: "min_discount_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("cb73475d-196b-5682-0376-1916b4bc0bb9"),
                column: "min_discount_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("cc5d2c86-bebf-f77e-e793-118285578549"),
                column: "min_discount_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("cfa77d27-5856-6281-d1af-51fa8c83c913"),
                column: "min_discount_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: new Guid("fa9f1398-0165-d599-8dfb-7880f4d3a479"),
                column: "min_discount_per_liter",
                value: null);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_pricing_knobs_non_negative",
                table: "fuel_types",
                sql: "(supplier_price_per_liter IS NULL OR supplier_price_per_liter >= 0) AND (pump_price_per_liter IS NULL OR pump_price_per_liter >= 0) AND (min_discount_per_liter IS NULL OR min_discount_per_liter >= 0)");
        }
    }
}
