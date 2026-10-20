using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheMeasurementsAreDecimalsNotFloats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A measurement is a decimal, not a float.
            //
            // Binary floating point cannot represent 0.1 exactly, and both of these columns are
            // compared against a threshold: a mismatch percentage against the line that separates a
            // warning from a failure, and a classifier's confidence against the floor below which a
            // message is dropped. A value that reads 10.0% and is stored as the double nearest 0.1 is
            // a value that is not the number anybody wrote down.
            //
            // These were the last two float columns in the schema that carry a measurement. Every
            // money column is already numeric with an explicit scale, and the coordinates -
            // brand_stations.lat and lng - stay double precision, because a coordinate IS a float.
            //
            // WHY NOW, AND WHY THIS MIGHT NOT BE FREE LATER. Changing a column's type rewrites the
            // table under an ACCESS EXCLUSIVE lock. fuel_vouchers holds ZERO rows in production
            // today, so the rewrite is free; after the first bulk import it is a rewrite of the
            // table that holds every voucher the platform has ever sold. That is the argument for
            // doing it before the first import rather than after, and the argument against doing it
            // later.
            migrationBuilder.AlterColumn<decimal>(
                name: "verification_mismatch_percent",
                table: "fuel_vouchers",
                type: "numeric(5,4)",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "confidence",
                table: "feedback_messages",
                type: "numeric(5,4)",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldPrecision: 3,
                oldScale: 2,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "verification_mismatch_percent",
                table: "fuel_vouchers",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "confidence",
                table: "feedback_messages",
                type: "double precision",
                precision: 3,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,4)",
                oldNullable: true);
        }
    }
}
