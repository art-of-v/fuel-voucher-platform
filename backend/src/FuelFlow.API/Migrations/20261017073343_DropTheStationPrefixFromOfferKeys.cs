using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Re-keys <c>fuel_offers</c> to the fuel id alone, and makes a fuel type's name unique per station.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What the ids looked like, and why that was wrong.</b> <c>SplitFuelPackagesIntoOffers</c> keyed
    /// an offer as <c>station_id || '-' || fuel_type_id</c>. The station was worth spelling out while a
    /// fuel id was a natural key of its own — but the seeded fuel ids already begin with the brand, so
    /// every seeded offer read <c>okko-okko-95</c>: the brand twice. And from the first operator-created
    /// fuel onwards the second half was a <c>Guid.CreateVersion7()</c> string
    /// (<c>BrandsController.AddFuel</c>), so the same column carried
    /// <c>okko-3808d410-b9ae-4511-9dfc-d6564fa2e1ad</c> beside <c>wog-wog-95</c>. Two shapes, one
    /// column, depending on which button produced the row.
    /// </para>
    /// <para>
    /// The key is now <c>fuel_type_id</c> with <c>-qa</c> for the rehearsal pool (<c>OfferKeys</c>). The
    /// station stays a column of the offer and stays part of its identity — it just stops being
    /// redundant: a fuel type belongs to exactly one station, so its id already determines the station.
    /// What that premise is worth is asked before every pull by
    /// <c>deploy/preflight-db/012-offer-station-matches-its-fuel-type.sql</c>, because no CHECK can span
    /// two tables.
    /// </para>
    /// <para>
    /// <b>Why this file is hand-written.</b> EF scaffolds this change as 20 <c>DeleteData</c> statements
    /// against <c>fuel_offers</c> plus 64 <c>UpdateData</c> against <c>fuel_offer_volumes</c>, because the
    /// catalogue is seeded through <c>HasData</c> and the seeded keys changed shape. On any database that
    /// is past the seed those rows are the real catalogue: deleting them would delete the prices the
    /// storefront sells at, which is the same mistake <c>SeedCatalogueAsOffers</c> refused to make.
    /// </para>
    /// <para>
    /// <b>Why a mapping table rather than two UPDATEs.</b> The volume rows point at offers by id, so the
    /// offers have to move before the volumes can follow — and once they have, <c>o.id = v.offer_id</c>
    /// matches nothing and the volumes are left pointing at keys that no longer exist. The old→new pairs
    /// are therefore materialised first, with the foreign key dropped across both updates and put back
    /// after. Re-adding it is not optional bookkeeping: it is what proves the re-key left every volume
    /// pointing at a real offer.
    /// </para>
    /// <para>
    /// <b>Both key shapes are accepted as input.</b> A fresh database has old-shape rows from the
    /// backfill and new-shape rows from <c>SeedCatalogueAsOffers</c>' sibling seeding path, so the guard
    /// below refuses only rows that are NEITHER — an id nothing can explain, which must not be silently
    /// renamed on the strength of a rule that never produced it.
    /// </para>
    /// </remarks>
    public partial class DropTheStationPrefixFromOfferKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Two guards before anything moves, both naming the rows rather than letting a constraint
            // report 23505 with no context.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    unrecognised text;
                BEGIN
                    SELECT string_agg(o.id, ', ' ORDER BY o.id) INTO unrecognised
                    FROM fuel_offers o
                    WHERE o.id <> o.fuel_type_id || CASE WHEN o.data_kind = 'qa' THEN '-qa' ELSE '' END
                      AND o.id <> o.station_id || '-' || o.fuel_type_id || CASE WHEN o.data_kind = 'qa' THEN '-qa' ELSE '' END;

                    IF unrecognised IS NOT NULL THEN
                        RAISE EXCEPTION
                            'fuel_offers carries id(s) that are neither the fuel type id nor the old station-prefixed key: %. '
                            'This migration re-keys rows by rule, so it will not guess what an unexplained id was; '
                            'reconcile these rows before retrying.',
                            unrecognised;
                    END IF;
                END $$;
                """);

            // The price of taking the station out of the key: it stops being able to separate two
            // offers that sell the SAME fuel type, which the old key could. That is legitimate data -
            // one fuel, two stations - and the fuel id alone cannot express it, so this says so instead
            // of dying on PK_fuel_offers half way through the UPDATE.
            //
            // In practice it means a row whose station disagrees with its fuel type's station, which
            // deploy/preflight-db/012 lists before a pull.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    collide text;
                BEGIN
                    SELECT string_agg(c.new_id || ' (' || c.offers || ' offers: ' || c.stations || ')', ', ')
                    INTO collide
                    FROM (
                        SELECT fuel_type_id || CASE WHEN data_kind = 'qa' THEN '-qa' ELSE '' END AS new_id,
                               count(*) AS offers,
                               string_agg(DISTINCT station_id, ', ') AS stations
                        FROM fuel_offers
                        GROUP BY 1
                        HAVING count(*) > 1
                    ) c;

                    IF collide IS NOT NULL THEN
                        RAISE EXCEPTION
                            'the fuel id alone cannot key these offers: %. '
                            'An offer key is now fuel_type_id (+ -qa), so one fuel type sold by two stations '
                            'collides. A fuel type belongs to exactly one station - check the rows where '
                            'fuel_offers.station_id <> fuel_types.station_id (deploy/preflight-db/012) and fix them first.',
                            collide;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE TEMPORARY TABLE offer_id_rekey ON COMMIT DROP AS
                SELECT id AS old_id,
                       fuel_type_id || CASE WHEN data_kind = 'qa' THEN '-qa' ELSE '' END AS new_id
                FROM fuel_offers
                WHERE id <> fuel_type_id || CASE WHEN data_kind = 'qa' THEN '-qa' ELSE '' END;
                """);

            // Dropped for the two UPDATEs and restored below: fuel_offer_volumes.offer_id is an
            // immediate foreign key, so a parent that moves out from under it violates the constraint
            // before the next statement can fix the child.
            //
            // Quoted, and that is not decoration: EF created this constraint with its capitalised name, and an
            // unquoted DROP folds to lower case - which is a 42704 on the first run of this migration.
            migrationBuilder.Sql(
                """
                ALTER TABLE fuel_offer_volumes
                    DROP CONSTRAINT "FK_fuel_offer_volumes_fuel_offers_offer_id";
                """);

            migrationBuilder.Sql(
                """
                UPDATE fuel_offers o
                SET id = m.new_id
                FROM offer_id_rekey m
                WHERE m.old_id = o.id;
                """);

            migrationBuilder.Sql(
                """
                UPDATE fuel_offer_volumes v
                SET offer_id = m.new_id
                FROM offer_id_rekey m
                WHERE m.old_id = v.offer_id;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE fuel_offer_volumes ADD CONSTRAINT "FK_fuel_offer_volumes_fuel_offers_offer_id"
                    FOREIGN KEY (offer_id) REFERENCES fuel_offers (id) ON DELETE RESTRICT;
                """);

            // The fuel type name is what identifies a fuel to a human, to the storefront and to the
            // voucher importer, and the fuel id stopped being that name when AddFuel began generating
            // it. Two live rows for one (station, name) is therefore two offers for one fuel, and
            // OkkoFuelClassifier/WogFuelClassifier pick between them by name with FirstOrDefault — so
            // the twin that never received the imported pump price is a landing place for a real
            // voucher. Refused here with the list, rather than by an opaque unique-index violation at
            // the end of the deploy; 011 asks the same question before the deploy.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    twins text;
                BEGIN
                    SELECT string_agg(t.station_id || '/' || t.name || ' (' || t.rows || ' rows)', ', ')
                    INTO twins
                    FROM (
                        SELECT station_id, name, count(*) AS rows
                        FROM fuel_types
                        WHERE NOT is_deleted
                        GROUP BY station_id, name
                        HAVING count(*) > 1
                    ) t;

                    IF twins IS NOT NULL THEN
                        RAISE EXCEPTION
                            'fuel_types carries more than one live row for: %. '
                            'The unique index below needs one live fuel per name per station; archive or merge these first.',
                            twins;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_fuel_types_station_id_name",
                table: "fuel_types");

            migrationBuilder.CreateIndex(
                name: "ux_fuel_types_station_name",
                table: "fuel_types",
                columns: new[] { "station_id", "name" },
                unique: true,
                filter: "NOT is_deleted");

            // Last, so that everything above runs against the old bounds and no statement has to reason
            // about a half-tightened column. 103 is the fuel id plus '-qa' - OfferKeyTests states the
            // same arithmetic.
            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offers_id_length",
                table: "fuel_offers");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offers_id_length",
                table: "fuel_offers",
                sql: "length(id) <= 103");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offer_volumes_offer_id_length",
                table: "fuel_offer_volumes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offer_volumes_offer_id_length",
                table: "fuel_offer_volumes",
                sql: "length(offer_id) <= 103");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_fuel_types_station_name",
                table: "fuel_types");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_types_station_id_name",
                table: "fuel_types",
                columns: new[] { "station_id", "name" });

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offers_id_length",
                table: "fuel_offers");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offers_id_length",
                table: "fuel_offers",
                sql: "length(id) <= 204");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offer_volumes_offer_id_length",
                table: "fuel_offer_volumes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offer_volumes_offer_id_length",
                table: "fuel_offer_volumes",
                sql: "length(offer_id) <= 204");

            // Back to the key SplitFuelPackagesIntoOffers wrote, and by the same route: pairs first,
            // foreign key down across both updates, foreign key back so the volumes cannot be left
            // pointing at a key that is not there.
            migrationBuilder.Sql(
                """
                CREATE TEMPORARY TABLE offer_id_rekey ON COMMIT DROP AS
                SELECT id AS old_id,
                       station_id || '-' || fuel_type_id || CASE WHEN data_kind = 'qa' THEN '-qa' ELSE '' END AS new_id
                FROM fuel_offers
                WHERE id <> station_id || '-' || fuel_type_id || CASE WHEN data_kind = 'qa' THEN '-qa' ELSE '' END;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE fuel_offer_volumes
                    DROP CONSTRAINT "FK_fuel_offer_volumes_fuel_offers_offer_id";
                """);

            migrationBuilder.Sql(
                """
                UPDATE fuel_offers o
                SET id = m.new_id
                FROM offer_id_rekey m
                WHERE m.old_id = o.id;
                """);

            migrationBuilder.Sql(
                """
                UPDATE fuel_offer_volumes v
                SET offer_id = m.new_id
                FROM offer_id_rekey m
                WHERE m.old_id = v.offer_id;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE fuel_offer_volumes ADD CONSTRAINT "FK_fuel_offer_volumes_fuel_offers_offer_id"
                    FOREIGN KEY (offer_id) REFERENCES fuel_offers (id) ON DELETE RESTRICT;
                """);
        }
    }
}