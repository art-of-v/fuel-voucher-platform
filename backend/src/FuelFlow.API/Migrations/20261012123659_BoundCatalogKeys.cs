using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class BoundCatalogKeys : Migration
    {
        /// <summary>
        /// The catalog's natural keys stay <c>text</c> - they are readable slugs like "okko-95", and
        /// converting a primary key's type would mean dropping and recreating every foreign key that
        /// points at it across seven tables. These CHECKs buy the part that actually matters: a slug
        /// cannot silently become a paragraph.
        /// <para>
        /// They run before the constraints are added, because a CHECK cannot be added over data that
        /// already violates it. Postgres would report <c>check constraint ... is violated by some row</c>,
        /// which names neither the table nor the offending value, so this names both. It reports and
        /// never truncates: a 200-character fuel type id is a data question, not something a migration
        /// should decide on the operator's behalf.
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
                            SELECT 'stations.id' AS col, id::text AS val FROM stations WHERE length(id) > 100
                            UNION ALL SELECT 'stations.name', name FROM stations WHERE length(name) > 200
                            UNION ALL SELECT 'fuel_types.id', id FROM fuel_types WHERE length(id) > 100
                            UNION ALL SELECT 'fuel_types.name', name FROM fuel_types WHERE length(name) > 200
                            UNION ALL SELECT 'fuel_types.station_id', station_id FROM fuel_types WHERE length(station_id) > 100
                            UNION ALL SELECT 'station_nodes.id', id FROM station_nodes WHERE length(id) > 100
                            UNION ALL SELECT 'station_nodes.station_id', station_id FROM station_nodes WHERE length(station_id) > 100
                            UNION ALL SELECT 'fuel_packages.id', id FROM fuel_packages WHERE length(id) > 100
                            UNION ALL SELECT 'fuel_packages.station_id', station_id FROM fuel_packages WHERE length(station_id) > 100
                            UNION ALL SELECT 'fuel_packages.fuel_type_id', fuel_type_id FROM fuel_packages WHERE length(fuel_type_id) > 100
                            UNION ALL SELECT 'fuel_vouchers.fuel_type_id', fuel_type_id FROM fuel_vouchers WHERE length(fuel_type_id) > 100
                            UNION ALL SELECT 'purchase_batches.fuel_type_id', fuel_type_id FROM purchase_batches WHERE length(fuel_type_id) > 100
                            UNION ALL SELECT 'voucher_exchanges.fuel_type_id', fuel_type_id FROM voucher_exchanges WHERE length(fuel_type_id) > 100
                            UNION ALL SELECT 'contracts.station_id', station_id FROM contracts WHERE station_id IS NOT NULL AND length(station_id) > 100
                        ) violations
                    LOOP
                        RAISE EXCEPTION
                            'BoundCatalogKeys: %.% exceeds 100 characters. Value: %',
                            split_part(offender.col, '.', 1), split_part(offender.col, '.', 2),
                            left(offender.val, 300);
                    END LOOP;
                END $$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_exchanges_fuel_type_id_length",
                table: "voucher_exchanges",
                sql: "length(fuel_type_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stations_id_length",
                table: "stations",
                sql: "length(id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stations_name_length",
                table: "stations",
                sql: "length(name) <= 200");

            migrationBuilder.AddCheckConstraint(
                name: "ck_station_nodes_id_length",
                table: "station_nodes",
                sql: "length(id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_station_nodes_station_id_length",
                table: "station_nodes",
                sql: "length(station_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_purchase_batches_fuel_type_id_length",
                table: "purchase_batches",
                sql: "length(fuel_type_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_vouchers_fuel_type_id_length",
                table: "fuel_vouchers",
                sql: "length(fuel_type_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_id_length",
                table: "fuel_types",
                sql: "length(id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_name_length",
                table: "fuel_types",
                sql: "length(name) <= 200");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_station_id_length",
                table: "fuel_types",
                sql: "length(station_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_packages_fuel_type_id_length",
                table: "fuel_packages",
                sql: "length(fuel_type_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_packages_id_length",
                table: "fuel_packages",
                sql: "length(id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_packages_station_id_length",
                table: "fuel_packages",
                sql: "length(station_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_station_id_length",
                table: "contracts",
                sql: "length(station_id) <= 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_exchanges_fuel_type_id_length",
                table: "voucher_exchanges");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stations_id_length",
                table: "stations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stations_name_length",
                table: "stations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_station_nodes_id_length",
                table: "station_nodes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_station_nodes_station_id_length",
                table: "station_nodes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_purchase_batches_fuel_type_id_length",
                table: "purchase_batches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_vouchers_fuel_type_id_length",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_id_length",
                table: "fuel_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_name_length",
                table: "fuel_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_station_id_length",
                table: "fuel_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_packages_fuel_type_id_length",
                table: "fuel_packages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_packages_id_length",
                table: "fuel_packages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_packages_station_id_length",
                table: "fuel_packages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_station_id_length",
                table: "contracts");
        }
    }
}
