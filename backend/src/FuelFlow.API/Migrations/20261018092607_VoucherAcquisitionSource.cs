using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class VoucherAcquisitionSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOT NULL with a default, in one statement. Postgres 11+ fills existing rows from a
            // non-volatile default without a table rewrite, so this is not an UPDATE-then-ALTER pair
            // and the lock is brief. Every voucher acquired before this column existed came through
            // the direct route, which makes the default a statement of fact rather than a placeholder
            // - and that is what allows NOT NULL here, where the settlement code's column could not be:
            // vouchers are created by many write paths (imports, manual entry, seeding) that have no
            // reason to mention this axis, and all of them get the correct value by not mentioning it.
            migrationBuilder.AddColumn<string>(
                name: "acquisition_source",
                table: "fuel_vouchers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "direct");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_vouchers_acquisition_source",
                table: "fuel_vouchers",
                sql: "acquisition_source IN ('direct', 'partner')");

            // Immutable, and enforced here rather than by leaving it to convention. The cost of a
            // voucher is a function of where it was acquired, so editing this value does not merely
            // relabel a row - it moves real money between two reports that are supposed to be
            // separate, and nothing downstream would notice. A voucher that turns out to have been
            // mislabelled at import is corrected by replacing the voucher, not by rewriting history.
            //
            // Only raises when the value actually differs, so the ordinary updates this table takes -
            // issuing, assigning, redeeming, costing - are untouched.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION trg_fuel_vouchers_acquisition_source_immutable() RETURNS trigger AS $$
                BEGIN
                    IF NEW.acquisition_source IS DISTINCT FROM OLD.acquisition_source THEN
                        RAISE EXCEPTION
                            'acquisition_source is set when a voucher is acquired and never changes (voucher %) - replace the voucher instead',
                            OLD.id;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER fuel_vouchers_acquisition_source_is_immutable
                    BEFORE UPDATE ON fuel_vouchers
                    FOR EACH ROW
                    EXECUTE FUNCTION trg_fuel_vouchers_acquisition_source_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS fuel_vouchers_acquisition_source_is_immutable ON fuel_vouchers;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS trg_fuel_vouchers_acquisition_source_immutable();");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_vouchers_acquisition_source",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "acquisition_source",
                table: "fuel_vouchers");
        }
    }
}