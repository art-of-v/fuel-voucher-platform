using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Archives the two fuels the OKKO catalogue import created on 2026-10-17, one of which was a
    /// duplicate grade and neither of which was named the way a grade is named.
    /// <para>
    /// The import matched what it already had by brand code alone. Production carried fifteen fuels and
    /// not one code, so every published grade looked missing and it added two rows on its first tick:
    /// <c>"ДП"</c> carrying <c>Pulls Diesel</c>, and <c>"95"</c> carrying <c>Pulls 95</c>. Both were named
    /// after the label the site happened to render rather than after the grade.
    /// </para>
    /// <para>
    /// <c>"ДП"</c> is a duplicate by grade: <c>"ДП PULLS"</c> already meant the same diesel and keeps the
    /// nominals an operator configured. <c>"95"</c> is a grade nobody sold, so archiving it loses
    /// nothing the next tick cannot recreate — correctly this time, as <c>"Пульс 95"</c>, once
    /// <c>ImportOkkoCatalogueCommandHandler</c> derives the name from the grade instead of from the
    /// label. The backfill then gives <c>"ДП PULLS"</c> the <c>Pulls Diesel</c> code it was refused
    /// before, because it is now the only fuel holding that grade.
    /// </para>
    /// <para>
    /// <b>Archived, not deleted</b> (#217): both had nominals pointed at them, and a delete that cascades
    /// into them would destroy the operator's work for the sake of tidiness. They leave the catalogue
    /// and the shelf the moment this runs and can be restored by clearing the flag.
    /// </para>
    /// <para>
    /// The <c>brand_fuel_code</c> predicate is the guard, not decoration: if either row has since been
    /// given a different code or sold against, this statement matches nothing and does nothing. That is
    /// deliberate — a data fix that has gone stale must be a no-op, not a second accident.
    /// </para>
    /// </summary>
    public partial class ArchiveTheImportedDuplicatesOfOneGrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE fuel_types
                   SET is_deleted = TRUE,
                       deleted_at_utc = now()
                 WHERE station_id = 'okko'
                   AND NOT is_deleted
                   AND brand_fuel_code IN ('Pulls Diesel', 'Pulls 95')
                   AND id IN ('01a14a6e-687d-76d1-8ab3-d36676ac582e',  -- "ДП", a duplicate of "ДП PULLS"
                              '01a14a6e-68d1-7ea1-a485-bb217afacbb2')  -- "95", recreatable as "Пульс 95"
                   AND NOT EXISTS (SELECT 1 FROM fuel_vouchers v WHERE v.fuel_type_id = fuel_types.id);

                UPDATE fuel_offers
                   SET is_deleted = TRUE,
                       deleted_at_utc = now()
                 WHERE NOT is_deleted
                   AND fuel_type_id IN ('01a14a6e-687d-76d1-8ab3-d36676ac582e',
                                        '01a14a6e-68d1-7ea1-a485-bb217afacbb2');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE fuel_offers
                   SET is_deleted = FALSE,
                       deleted_at_utc = NULL
                 WHERE fuel_type_id IN ('01a14a6e-687d-76d1-8ab3-d36676ac582e',
                                        '01a14a6e-68d1-7ea1-a485-bb217afacbb2');

                UPDATE fuel_types
                   SET is_deleted = FALSE,
                       deleted_at_utc = NULL
                 WHERE id IN ('01a14a6e-687d-76d1-8ab3-d36676ac582e',
                              '01a14a6e-68d1-7ea1-a485-bb217afacbb2');
                """);
        }
    }
}
