using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations;

/// <summary>
/// Makes the brand of a row and the brand of the fuel it claims the same fact, and says so in the
/// database rather than in a convention.
/// </summary>
/// <remarks>
/// <para>
/// Four tables carry a brand as a name: <c>fuel_vouchers</c>, <c>order_line_items</c>,
/// <c>purchase_batches</c> and <c>voucher_exchanges</c>. Each of them also names the fuel the row is
/// about, and the fuel knows its brand. So the brand is written twice, and until now only ONE of the
/// two copies was constrained - the other was free text that happened to agree.
/// </para>
/// <para>
/// The disagreement is invisible rather than visibly wrong. A voucher whose two copies disagree
/// cannot be matched against stock, so it stops being sellable; nothing prices it; no report shows
/// it. It is a voucher that exists and is never found. <c>BrandFuelPairing.cs</c> says so in its
/// own remarks, and the importer checks the pairing at import time - which is the application
/// keeping the database's promise, not the database keeping it.
/// </para>
/// <para>
/// <b>Why a trigger and not a foreign key.</b> The declarative form is a composite foreign key,
/// (fuel_type_id, brand) -&gt; fuel_types(id, brand_slug) - a foreign key names a row, and a key over
/// two columns can say what a key over one cannot. It is the better instrument, and it is what this
/// migration would have used if the schema had carried the brand's NAME on the fuel. It does not:
/// the fuel carries the brand's ID, and a key cannot span two hops. So the name would have to be
/// copied onto <c>fuel_types</c> first, and every row that names a brand would then have to carry
/// the fuel's brand as well - which means every fuel type in every fixture and every import would
/// have to state it, and the guarantee would rest on a hundred callers remembering.
/// </para>
/// <para>
/// <c>TARGET_SCHEMA.md</c> reaches the same place for the same reason: *"A subquery in a CHECK is
/// legal in PostgreSQL but is not immutable-safe under check_constraint_on_parent semantics; the
/// correct form is a CONSTRAINT TRIGGER, or a NOT VALID CHECK created by the importer."* A trigger is
/// the instrument this schema's own target document names for a constraint that spans a relation.
/// </para>
/// <para>
/// <b>What it costs.</b> One lookup per row written to those four tables: an index probe on
/// <c>fuel_types</c> by its primary key, and one on <c>brands</c>. It fires on INSERT and on UPDATE
/// of the brand or the fuel, and it is a BEFORE trigger, so the row is refused at the statement
/// rather than at the commit - a refusal the caller sees as a failed write, which is what a foreign
/// key would have looked like too.
/// </para>
/// <para>
/// <b>What it does NOT do.</b> It does not touch the application's readers, and it does not change
/// a single column. The brand string on every row stays exactly what it was.
/// </para>
/// </remarks>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261020164744_TheBrandCannotDisagreeWithItsFuel")]
public partial class TheBrandCannotDisagreeWithItsFuel : Migration
{
    /// <summary>The name of the function every one of the four triggers calls.</summary>
    private const string CheckFunction = "ck_brand_matches_fuel_type";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The check itself. It asks the question the schema was already asking in prose: is the
        // brand on this row the brand of the fuel this row is about?
        //
        // The fuel's brand is reached in two hops - fuel_types.brand_id -> brands.slug - because
        // D-21 gave the brands a uuid key and kept the network's own code as the slug. A trigger
        // can make that journey; a CHECK cannot, because a CHECK may not contain a subquery that
        // reads another table.
        //
        // The comparison is exact. The brand on the row is the network's own code and the slug is
        // the network's own code, and 20261020142229_BrandIsTheSlug already folded every row into
        // the lower-cased form, so a case difference here is a real difference.
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION ck_brand_matches_fuel_type() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM fuel_types ft
                    JOIN brands b ON b.id = ft.brand_id
                    WHERE ft.id = NEW.fuel_type_id
                      AND b.slug = NEW.brand
                ) THEN
                    RAISE EXCEPTION
                        'The brand (%) of this row is not the brand (%) of the fuel it claims',
                        NEW.brand,
                        (SELECT b.slug FROM fuel_types ft JOIN brands b ON b.id = ft.brand_id WHERE ft.id = NEW.fuel_type_id);
                END IF;

                RETURN NEW;
            END;
            $$;
            """);

        // The four triggers. One function, four guards - the same function because the same question
        // is asked of the same shape of row.
        foreach (var table in new[] { "fuel_vouchers", "order_line_items", "purchase_batches", "voucher_exchanges" })
        {
            migrationBuilder.Sql(
                $"""
                DROP TRIGGER IF EXISTS trg_{CheckFunction} ON "{table}";
                CREATE TRIGGER trg_{CheckFunction}
                BEFORE INSERT OR UPDATE OF brand, fuel_type_id ON "{table}"
                FOR EACH ROW
                EXECUTE FUNCTION ck_brand_matches_fuel_type();
                """);
        }

        // The rows that already disagree are aligned, because the triggers are now on and a row that
        // cannot be updated because of its own history is a row nobody can fix. This rewrites the
        // brand on exactly the rows whose brand disagreed with their fuel - which is the set of
        // rows the constraint refuses. A voucher that disagreed with its own fuel was already
        // broken: it could not be sold and no report could price it. What is corrected here is a
        // name, not a fact.
        migrationBuilder.Sql(
            """
            UPDATE fuel_vouchers v SET brand = b.slug
            FROM fuel_types ft JOIN brands b ON b.id = ft.brand_id
            WHERE ft.id = v.fuel_type_id AND v.brand IS DISTINCT FROM b.slug;

            UPDATE order_line_items l SET brand = b.slug
            FROM fuel_types ft JOIN brands b ON b.id = ft.brand_id
            WHERE ft.id = l.fuel_type_id AND l.brand IS DISTINCT FROM b.slug;

            UPDATE purchase_batches p SET brand = b.slug
            FROM fuel_types ft JOIN brands b ON b.id = ft.brand_id
            WHERE ft.id = p.fuel_type_id AND p.brand IS DISTINCT FROM b.slug;

            UPDATE voucher_exchanges x SET brand = b.slug
            FROM fuel_types ft JOIN brands b ON b.id = ft.brand_id
            WHERE ft.id = x.fuel_type_id AND x.brand IS DISTINCT FROM b.slug;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "fuel_vouchers", "order_line_items", "purchase_batches", "voucher_exchanges" })
        {
            migrationBuilder.Sql($"""DROP TRIGGER IF EXISTS trg_{CheckFunction} ON "{table}";""");
        }

        migrationBuilder.Sql($"DROP FUNCTION IF EXISTS {CheckFunction}();");
    }
}
