using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class BoundCatalogDisplayColumns : Migration
    {
        /// <summary>
        /// The raw SQL that opens <c>Up()</c>, before any ALTER.
        /// <para>
        /// This migration adds a length limit to fifteen columns that were unbounded <c>text</c>, and
        /// unlike a CHECK or a foreign key an <c>ALTER COLUMN ... TYPE varchar(n)</c> fails on a
        /// value that is already too long. Postgres reports that as
        /// <c>value too long for type character varying(n)</c> — which names neither the table, nor
        /// the column, nor the row, so an operator would have no way to find what to fix.
        /// </para>
        /// <para>
        /// So this runs first and says exactly which row is the problem. It reports, it never repairs:
        /// a station name that is genuinely 400 characters is a data-entry question for someone who
        /// knows the catalog, not something a migration should silently truncate.
        /// </para>
        /// <para>
        /// <b>The limits are not validated against real data.</b> They were derived from the domain —
        /// a hex colour is 7 characters, a seeded logo_text is "OKKO", E.164 fits in 15 — and carry
        /// generous headroom, but no production-like dataset was reachable to confirm them. That is
        /// precisely why this precheck names the offending value instead of assuming there is none.
        /// </para>
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    offender record;
                BEGIN
                    FOR offender IN
                        SELECT * FROM (
                            SELECT 'voucher_imports.status' AS col, 32 AS lim, status::text AS val FROM voucher_imports WHERE length(status) > 32
                            UNION ALL SELECT 'voucher_imports.file_name', 500, file_name FROM voucher_imports WHERE length(file_name) > 500
                            UNION ALL SELECT 'stations.name', 200, name FROM stations WHERE length(name) > 200
                            UNION ALL SELECT 'stations.color', 16, color FROM stations WHERE length(color) > 16
                            UNION ALL SELECT 'stations.logo_text', 64, logo_text FROM stations WHERE length(logo_text) > 64
                            UNION ALL SELECT 'stations.address', 500, address FROM stations WHERE address IS NOT NULL AND length(address) > 500
                            UNION ALL SELECT 'stations.phone', 45, phone FROM stations WHERE phone IS NOT NULL AND length(phone) > 45
                            UNION ALL SELECT 'stations.station_type', 64, station_type FROM stations WHERE station_type IS NOT NULL AND length(station_type) > 64
                            UNION ALL SELECT 'station_nodes.name', 200, name FROM station_nodes WHERE length(name) > 200
                            UNION ALL SELECT 'station_nodes.address', 500, address FROM station_nodes WHERE address IS NOT NULL AND length(address) > 500
                            UNION ALL SELECT 'station_nodes.phone', 45, phone FROM station_nodes WHERE phone IS NOT NULL AND length(phone) > 45
                            UNION ALL SELECT 'station_nodes.city', 128, city FROM station_nodes WHERE city IS NOT NULL AND length(city) > 128
                            UNION ALL SELECT 'station_nodes.station_type', 64, station_type FROM station_nodes WHERE station_type IS NOT NULL AND length(station_type) > 64
                            UNION ALL SELECT 'fuel_packages.fuel_name', 200, fuel_name FROM fuel_packages WHERE length(fuel_name) > 200
                        ) violations
                    LOOP
                        RAISE EXCEPTION
                            'BoundCatalogDisplayColumns: % exceeds the % character limit for its column. Value: %',
                            offender.col, offender.lim, left(offender.val, 300);
                    END LOOP;
                END $$;
                """);
            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "voucher_imports",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "file_name",
                table: "voucher_imports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "station_type",
                table: "stations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "stations",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "stations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "logo_text",
                table: "stations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "color",
                table: "stations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "#00ff80",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "#00ff80");

            migrationBuilder.AlterColumn<string>(
                name: "address",
                table: "stations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "station_type",
                table: "station_nodes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "station_nodes",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "station_nodes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "city",
                table: "station_nodes",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "address",
                table: "station_nodes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "fuel_name",
                table: "fuel_packages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "voucher_imports",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "file_name",
                table: "voucher_imports",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "station_type",
                table: "stations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "stations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(45)",
                oldMaxLength: 45,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "stations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "logo_text",
                table: "stations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "color",
                table: "stations",
                type: "text",
                nullable: false,
                defaultValue: "#00ff80",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "#00ff80");

            migrationBuilder.AlterColumn<string>(
                name: "address",
                table: "stations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "station_type",
                table: "station_nodes",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "station_nodes",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(45)",
                oldMaxLength: 45,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "station_nodes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "city",
                table: "station_nodes",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "address",
                table: "station_nodes",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "fuel_name",
                table: "fuel_packages",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }
    }
}
