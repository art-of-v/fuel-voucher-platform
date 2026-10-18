using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations;

/// <summary>
/// Gives the hand-typed UPG and WOG fuels the brand's own code for each grade.
/// </summary>
/// <remarks>
/// <para>
/// These two brands publish a token per grade, and on their sites that token <b>is</b> the name
/// they show their customers: UPG prints <c>upg95</c> and <c>EURO DIESEL</c>, WOG prints
/// <c>ДП Євро5</c> and <c>95 Євро5-Е10</c>. Our rows were typed from those very pages, so their
/// names already equal the codes — which is why this is an assignment and not a table of pairs.
/// Hardcoding an id-to-code list here would be both longer and wrong the moment a row is renamed.
/// </para>
/// <para>
/// <b>Why it has to happen before the first import.</b> <c>ImportUpgWogPricesCommandHandler</c>
/// matches on <c>brand_fuel_code</c> alone, deliberately: these brands sell two distinct 95s and two
/// distinct diesels, so a name- or category-based fallback would hand both prices to whichever row
/// the query returned first. Run against uncoded rows it would therefore create a second copy of
/// every grade we already sell, which is the duplicate this whole change exists to end.
/// </para>
/// <para>
/// <b>Scoped to upg and wog on purpose.</b> KLO has no published fuel price list, so nothing here can
/// say what its codes are; those five rows stay uncoded until somebody with the price sheet decides.
/// A brand with no code keeps working exactly as before — it is simply invisible to the import.
/// </para>
/// </remarks>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261018221000_BrandFuelCodesForUpgAndWog")]
public partial class BrandFuelCodesForUpgAndWog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE fuel_types
            SET brand_fuel_code = btrim(name)
            WHERE brand_id IN ('upg', 'wog')
              AND brand_fuel_code IS NULL
              AND NOT is_deleted
              AND btrim(name) <> '';

            -- The index is (brand_id, brand_fuel_code) partial on brand_fuel_code IS NOT NULL, so two
            -- grades of one brand sharing a name would now collide with it. That is the point: the
            -- duplicate would be refused rather than created, and the migration fails loudly here
            -- instead of leaving half the catalogue coded.
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE fuel_types
            SET brand_fuel_code = NULL
            WHERE brand_id IN ('upg', 'wog')
              AND brand_fuel_code IS NOT NULL
              AND brand_fuel_code = btrim(name);
            """);
    }
}
