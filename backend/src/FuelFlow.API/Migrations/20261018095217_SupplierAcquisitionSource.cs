using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class SupplierAcquisitionSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "acquisition_source",
                table: "suppliers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "direct");

            migrationBuilder.AddCheckConstraint(
                name: "ck_suppliers_acquisition_source",
                table: "suppliers",
                sql: "acquisition_source IN ('direct', 'partner')");

            // Immutable for the same reason the voucher's is, from the opposite direction. Every
            // voucher already carries a copy of this value, so an editable supplier would let a
            // correction made today retroactively disagree with vouchers imported months ago - and
            // nothing would report the disagreement, because both columns would each be individually
            // valid. A supplier that genuinely changes route is a new supplier row.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION trg_suppliers_acquisition_source_immutable() RETURNS trigger AS $$
                BEGIN
                    IF NEW.acquisition_source IS DISTINCT FROM OLD.acquisition_source THEN
                        RAISE EXCEPTION
                            'acquisition_source describes the supply relationship and never changes (supplier %) - add a new supplier instead',
                            OLD.id;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER suppliers_acquisition_source_is_immutable
                    BEFORE UPDATE ON suppliers
                    FOR EACH ROW
                    EXECUTE FUNCTION trg_suppliers_acquisition_source_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS suppliers_acquisition_source_is_immutable ON suppliers;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS trg_suppliers_acquisition_source_immutable();");

            migrationBuilder.DropCheckConstraint(
                name: "ck_suppliers_acquisition_source",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "acquisition_source",
                table: "suppliers");
        }
    }
}
