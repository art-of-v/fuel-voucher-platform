using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations;

/// <summary>
/// Gives the UPG and WOG fuels whose name is the brand's own published token that token as a code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this has to happen before the first import.</b>
/// <c>ImportUpgWogPricesCommandHandler</c> matches on <c>brand_fuel_code</c> alone, deliberately:
/// these brands sell two distinct 95s and two distinct diesels, so a name- or category-based
/// fallback would hand both prices to whichever row the query returned first. Run against uncoded
/// rows it would create a second copy of every grade we already sell, which is the duplicate the
/// same change deleted a route to prevent.
/// </para>
/// <para>
/// <b>Why the tokens are listed rather than taken from the name.</b> An earlier version of this
/// migration assigned <c>brand_fuel_code = btrim(name)</c> for both brands, which reads like a
/// simplification and is not one. WOG's vocabulary does not match our rows: the brand publishes
/// <c>ДП Mustang+</c>, <c>95 Mustang Євро5-Е10</c> and <c>100 Mustang Євро5-Е0</c>, while our seeded
/// fuels are named <c>ДП Mustang</c>, <c>A-95 Mustang</c>, <c>A 95 EURO</c> and <c>Mustang 100</c>.
/// Assigning those names would have written four codes WOG has never published - and the unique
/// index would then have enforced those guesses as identities. The integration test
/// <c>TheSeededWogFuels_CarryNoCode</c> exists precisely to stop that, and it caught it.
/// </para>
/// <para>
/// <b>So a row is coded only when its name is a token the brand actually publishes</b>, and the
/// tokens are listed here because the brand's site is the only authority on them. Everything else
/// stays uncoded and keeps working exactly as before - an uncoded fuel is simply invisible to the
/// import, and prices by category until somebody with the price sheet decides otherwise.
/// </para>
/// <para>
/// KLO is absent by the same reasoning: it has a stations fetcher but no published fuel price list,
/// so nothing in this repository knows its tokens. Its five fuels stay uncoded.
/// </para>
///
/// <para>
/// <b>The brand is named by its slug, resolved in the statement.</b> This migration sorts after
/// 20261018191320_CatalogueBrandsBecomeUuid, which made <c>fuel_types.brand_id</c> a uuid; the two
/// predicates below used to spell <c>'upg'</c> and <c>'wog'</c> directly, which is now an invalid
/// uuid literal and aborts the deploy. A brand that is not in the catalogue matches nothing, which
/// is the right answer for a backfill.
/// </para>
/// </remarks>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261018221000_BrandFuelCodesForUpgAndWog")]
public partial class BrandFuelCodesForUpgAndWog : Migration
{
    /// <summary>The grades each brand publishes, as read off its own price sheet.</summary>
    /// <remarks>
    /// AdBlue is deliberately absent from both. UPG shows it on the page but not in the price blob
    /// the fetcher reads, and we do not sell it.
    /// </remarks>
    private static readonly string[] UpgTokens =
    [
        "upg100", "upg95", "A-95", "EURO DIESEL", "upgDIESEL", "Газ"
    ];

    private static readonly string[] WogTokens =
    [
        "ДП Євро5", "ДП Mustang+", "95 Євро5-Е10", "95 Mustang Євро5-Е10",
        "100 Mustang Євро5-Е0", "ГАЗ", "AdBlue"
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""
             UPDATE fuel_types
             SET brand_fuel_code = btrim(name)
             WHERE brand_id = (SELECT id FROM brands WHERE slug = 'upg')
               AND brand_fuel_code IS NULL
               AND NOT is_deleted
               AND btrim(name) = ANY (ARRAY[{Quoted(UpgTokens)}]);

             UPDATE fuel_types
             SET brand_fuel_code = btrim(name)
             WHERE brand_id = (SELECT id FROM brands WHERE slug = 'wog')
               AND brand_fuel_code IS NULL
               AND NOT is_deleted
               AND btrim(name) = ANY (ARRAY[{Quoted(WogTokens)}]);
             """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""
             UPDATE fuel_types
             SET brand_fuel_code = NULL
             WHERE brand_id = (SELECT id FROM brands WHERE slug = 'upg')
               AND brand_fuel_code IS NOT NULL
               AND brand_fuel_code = ANY (ARRAY[{Quoted(UpgTokens)}]);

             UPDATE fuel_types
             SET brand_fuel_code = NULL
             WHERE brand_id = (SELECT id FROM brands WHERE slug = 'wog')
               AND brand_fuel_code IS NOT NULL
               AND brand_fuel_code = ANY (ARRAY[{Quoted(WogTokens)}]);
             """);
    }

    /// <summary>
    /// Renders tokens as SQL string literals. Quote doubling rather than parameters, because
    /// <c>migrationBuilder.Sql</c> here is a raw script and the tokens are compile-time constants -
    /// but "compile-time constant" is exactly when an interpolated string looks most innocent.
    /// </summary>
    private static string Quoted(string[] tokens)
        => string.Join(", ", tokens.Select(t => $"'{t.Replace("'", "''")}'"));
}