using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class StationNamesSortTheWayPeopleWriteThem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The function comes first: the column below is defined in terms of it, and a generated
            // column cannot be created before the function it calls exists.
            //
            // It pads every run of digits to a fixed width, so the byte order of the result is the
            // order a person would write: "№1", "№2", "№10", "№100" rather than "№1", "№10",
            // "№100", "№2". The padding has to be FIXED width, and the whole of that is one
            // detail: a naive `lpad` on the captured digits is not, because the capture is not
            // available to a SQL expression - `length('\1')` is the length of the two-character
            // string, not of the number - and a variable-width pad sorts "№2" before "№10".
            //
            // The text between the numbers is left exactly as it is, so the column's own collation
            // does the rest: the database collates in Ukrainian, and that is what sorts "KLO
            // Березань" before "KLO Борислав" and Ґ where it belongs.
            //
            // IMMUTABLE and STRICT because a generated column may only use immutable functions, and
            // STRICT because a NULL name has no key to speak of.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION fuelflow_natural_sort_key(text) RETURNS text
                LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE AS $fn$
                    SELECT coalesce(string_agg(
                             CASE WHEN m[1] ~ '^[0-9]+$' THEN lpad(m[1], 10, '0') ELSE m[1] END,
                             '' ORDER BY ord), '')
                      FROM regexp_matches($1, '([0-9]+|[^0-9]+)', 'g') WITH ORDINALITY AS t(m, ord)
                $fn$;
                """);

            migrationBuilder.AddColumn<string>(
                name: "name_sort_key",
                table: "brand_stations",
                type: "text",
                nullable: true,
                computedColumnSql: "fuelflow_natural_sort_key(name)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_brand_stations_name_sort_key",
                table: "brand_stations",
                column: "name_sort_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_brand_stations_name_sort_key",
                table: "brand_stations");

            migrationBuilder.DropColumn(
                name: "name_sort_key",
                table: "brand_stations");

            // The function goes with the column: nothing else calls it, and leaving it behind
            // would mean a database carrying a function whose only purpose has been removed.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fuelflow_natural_sort_key(text);");
        }
    }
}
