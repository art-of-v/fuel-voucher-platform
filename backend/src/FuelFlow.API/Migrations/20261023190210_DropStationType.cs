using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class DropStationType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The column that said what KIND of place a station is - "Тип АЗС ОККО-міська",
            // "Тип АЗС WOG-міська" - goes with the column itself (planning #680).
            //
            // It was never a fact about the place. It is a label the network's file carried, and the
            // catalogue never had it: measured on production on 2026-10-21, 1,321 of the 1,322
            // stations carried no type at all and the one that did said "Тип АЗС ОККО-міська",
            // which is a phrase about the network rather than about the station. Nothing read it:
            // the storefront showed it under the name, the admin imported it, and no decision was
            // ever made on it.
            //
            // The owner's decision (planning #680): a station is its brand, its number and its
            // address. Everything else on the row is a fact we keep because something uses it - the
            // coordinates place it on the map, the phone lets a customer call it - and this had
            // neither.
            //
            // The values are not migrated anywhere, and that is the point: there is nowhere they
            // belong. The Down below restores the COLUMN, not the rows - a rollback puts the schema
            // back, and the labels are read again from the network's file, which is where they came
            // from in the first place.
            migrationBuilder.DropColumn(
                name: "station_type",
                table: "brand_stations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "station_type",
                table: "brand_stations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }
    }
}
