using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// The catalogue says a grade once: one live row per brand per grade, even when the grade is
    /// spelled two ways.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this adds.</b> <c>fuel_types.grade_key</c> - the fuel's name folded onto one
    /// spelling - and a unique index on (brand, key). "А-95" and "A-95" are one grade in two
    /// spellings, and the index on the name could not tell them apart, so both were accepted and
    /// the catalogue grew a second row for a fuel it already sells: two rows the storefront lists
    /// at two prices, and a voucher that can be priced against whichever one the importer picked.
    /// Planning #349 carries the owner's decision (2026-10-21): they are one grade, and the
    /// catalogue says so once.
    /// </para>
    /// <para>
    /// <b>Why the database computes the key.</b> The key is a GENERATED column, so it is the same
    /// for every row whatever wrote it: a column the application writes is a column a path can
    /// forget, and a path that forgets writes a twin. The rule itself is
    /// <c>fuelflow_fuel_grade_key(text)</c>, and the application applies the same rule in
    /// <c>SharedKernel.Domain.FuelGrade</c> - the two are read side by side over every name the
    /// catalogue holds, by a test, because a constraint that computes a different key from the one
    /// the application believes in would refuse a fuel the application believes is a different
    /// grade.
    /// </para>
    /// <para>
    /// <b>Why it refuses rather than merges.</b> Two rows that mean one grade cannot be merged by
    /// a migration: one of them carries the prices, the offers and the vouchers, and which one
    /// survives is a decision about the business, not about the schema. So the rows are named and
    /// the deploy stops - and <c>deploy/preflight-db/018</c> asks the same question thirty
    /// seconds before the image is pulled.
    /// </para>
    /// </remarks>
    public partial class TheSecondSpellingIsTheSameGrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The function comes first: the column below is defined in terms of it, and a generated
            // column cannot be created before the function it calls exists.
            //
            // IMMUTABLE and STRICT because a generated column may only use immutable functions, and
            // STRICT because a name with no key has no grade to speak of.
            //
            // The rule, in order: fold the Cyrillic letters onto their Latin twins, lower-case what
            // is left, drop everything that is not a letter, a digit or a plus, and spell the four
            // words that have a standard Latin spelling the way the classifier spells them - so that
            // "Пульс 95" and "Pulls 95" are one key, and "ДП" and "DP" are one key.
            //
            // The plus survives, because "А 95+" and "А 95" are two grades at KLO, and a rule that
            // merged them would refuse a fuel the network really sells. Merging too much is the
            // dangerous direction here: this index refuses a row.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION fuelflow_fuel_grade_key(text) RETURNS text
                LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE AS $fn$
                    SELECT NULLIF(
                        replace(replace(replace(replace(
                            regexp_replace(
                                lower(translate($1,
                                    'АБВГҐДЕЄЁЖЗИІЇЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгґдеєёжзиіїйклмнопрстуфхцчшщъыьэюя',
                                    'ABVGGDEEEJZIIIJKLMNOPRSTUFHCCSS_Y_EUAabvggdeeejziiijklmnoprstufhccss_y_eua')),
                                '[^a-z0-9+]', '', 'g'),
                            'puls', 'pulls'),
                        'dizel', 'diesel'),
                        'gaz', 'gas'),
                        'evro', 'euro'),
                    '')
                $fn$;
                """);

            migrationBuilder.AddColumn<string>(
                name: "grade_key",
                table: "fuel_types",
                type: "text",
                nullable: true,
                computedColumnSql: "fuelflow_fuel_grade_key(name)",
                stored: true,
                comment: "The grade's key: the name folded onto one spelling, so that 'А-95' and 'A-95' are one grade. Computed by the database from fuelflow_fuel_grade_key(name), the same rule SharedKernel.Domain.FuelGrade applies; NULL when the name says no grade at all.");

            // The guard, before the index: the index would refuse to be created, and it would say
            // 23505 with the name of the index and nothing else. This says which rows are one
            // grade in two spellings, and why the migration will not decide for itself which of
            // them survives - one of them carries the prices, the offers and the vouchers.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    twins text;
                BEGIN
                    SELECT string_agg(
                             b.slug || ': "' || f.name || '" (' || f.id || ') and "'
                                 || g.name || '" (' || g.id || ')',
                             ', ' ORDER BY b.slug, f.name)
                    INTO twins
                    FROM fuel_types f
                    JOIN fuel_types g
                      ON g.brand_id = f.brand_id
                     AND g.grade_key = f.grade_key
                     AND g.id > f.id
                    JOIN brands b ON b.id = f.brand_id
                    WHERE NOT f.is_deleted
                      AND NOT g.is_deleted
                      AND f.grade_key IS NOT NULL;

                    IF twins IS NOT NULL THEN
                        RAISE EXCEPTION
                            'these live fuels are one grade at one brand, spelled two ways: %. '
                            'The catalogue says a grade once (planning #349), and this migration will not '
                            'merge them: which row survives is a decision about the prices, the offers and the '
                            'vouchers, not about the schema. Merge them by hand, or archive one of them, and retry.',
                            twins;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_fuel_types_brand_grade_key",
                table: "fuel_types",
                columns: new[] { "brand_id", "grade_key" },
                unique: true,
                filter: "NOT is_deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_fuel_types_brand_grade_key",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "grade_key",
                table: "fuel_types");

            // The function goes with the column: nothing else calls it, and leaving it behind would
            // mean a database carrying a function whose only purpose has been removed.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fuelflow_fuel_grade_key(text);");
        }
    }
}
