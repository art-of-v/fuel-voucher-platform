using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations;

/// <summary>
/// Turns the "may be sold below cost" switch off for every fuel.
/// </summary>
/// <remarks>
/// <para>
/// The column's default is false and always was; the DATA says otherwise. Every one of the
/// twenty-three fuels carried <c>allow_below_cost = true</c>, which reads as "this fuel may be
/// sold below cost" - for the whole catalogue, and with no supplier cost recorded against any
/// of them, so there was nothing to be below.
/// </para>
/// <para>
/// It is vacuous until the costs go in, and then it is not. The moment a cost is recorded, the
/// below-cost guard switches off for every fuel: the checkout asks <c>if
/// (!fuel.AllowBelowCost &amp;&amp; verdict == BelowCost)</c>, and the imports' own guard - the
/// one that stops a third party's price move from taking a fuel off sale unattended - never
/// fires either.
/// </para>
/// <para>
/// A catalogue priced while every fuel is allowed below cost is a catalogue that sells below
/// cost. So the switch goes off FIRST, and the costs go in afterwards.
/// </para>
/// <para>
/// A hand-written migration, and the model does not change: the column's default is already
/// false, so the model and the schema were never out of step - only the rows were.
/// </para>
/// </remarks>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261021070000_TheBelowCostSwitchGoesOff")]
public partial class TheBelowCostSwitchGoesOff : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE fuel_types SET allow_below_cost = false WHERE allow_below_cost;
            """);

        // A fuel with no cost has nothing to be below, so the switch on it means nothing - and
        // it means something the moment a cost is recorded. The rule belongs in the schema: the
        // switch can only be on for a fuel whose cost is known.
        migrationBuilder.Sql("""
            ALTER TABLE fuel_types ADD CONSTRAINT ck_fuel_types_allow_below_cost_needs_a_cost
                CHECK (allow_below_cost = false OR supplier_price_per_liter IS NOT NULL);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE fuel_types DROP CONSTRAINT ck_fuel_types_allow_below_cost_needs_a_cost;
            """);
    }
}
