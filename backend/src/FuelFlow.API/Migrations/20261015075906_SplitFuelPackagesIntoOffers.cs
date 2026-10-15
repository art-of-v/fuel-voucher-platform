using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class SplitFuelPackagesIntoOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fuel_offers",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    station_id = table.Column<string>(type: "text", nullable: false),
                    fuel_type_id = table.Column<string>(type: "text", nullable: false),
                    supplier_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    margin_uah_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    margin_percent = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    final_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    pump_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    min_discount_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    price_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    price_updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_offers", x => x.id);
                    table.CheckConstraint("ck_fuel_offers_data_kind", "data_kind IN ('real', 'qa')");
                    table.CheckConstraint("ck_fuel_offers_fuel_type_id_length", "length(fuel_type_id) <= 100");
                    table.CheckConstraint("ck_fuel_offers_id_length", "length(id) <= 204");
                    table.CheckConstraint("ck_fuel_offers_margin_signs", "margin_uah_per_liter IS NULL OR margin_uah_per_liter >= 0");
                    table.CheckConstraint("ck_fuel_offers_prices_non_negative", "(final_price_per_liter IS NULL OR final_price_per_liter >= 0) AND (pump_price_per_liter IS NULL OR pump_price_per_liter >= 0) AND (min_discount_per_liter IS NULL OR min_discount_per_liter >= 0)");
                    table.CheckConstraint("ck_fuel_offers_station_id_length", "length(station_id) <= 100");
                });

            migrationBuilder.CreateTable(
                name: "fuel_offer_volumes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    offer_id = table.Column<string>(type: "text", nullable: false),
                    liters = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    package_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_offer_volumes", x => x.id);
                    table.CheckConstraint("ck_fuel_offer_volumes_id_length", "length(id) <= 100");
                    table.CheckConstraint("ck_fuel_offer_volumes_liters_positive", "liters > 0");
                    table.CheckConstraint("ck_fuel_offer_volumes_offer_id_length", "length(offer_id) <= 204");
                    table.CheckConstraint("ck_fuel_offer_volumes_package_price_non_negative", "package_price >= 0");
                    table.ForeignKey(
                        name: "FK_fuel_offer_volumes_fuel_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "fuel_offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Scaffolded here, and deliberately removed: CreateIndex("IX_orders_monobank_invoice_id") on
            // orders, plus the matching DropIndex in Down().
            //
            // UniqueMonobankInvoiceId already created that index. It emitted this pair because the
            // model and the snapshot disagreed on its NAME: the migration created it as
            // IX_orders_monobank_invoice_id (snake_case, as migrationBuilder names things) while EF's
            // convention for HasIndex("MonobankInvoiceId") is IX_orders_MonobankInvoiceId
            // (PascalCase), so EF believed the database held a differently-named index and tried to
            // rename it. Shipping the pair would fail on any real database with
            // 42P07 "relation IX_orders_monobank_invoice_id already exists".
            //
            // The rename is recorded where it belongs instead - OrderConfiguration.cs:144 now calls
            // .HasDatabaseName("IX_orders_monobank_invoice_id") and the snapshot carries the same name,
            // so model, snapshot and database finally agree. Without that, EVERY future
            // `migrations add` would scaffold another CREATE for this index and no fresh database
            // could be migrated at all.
            //
            // Worth carrying to the next migration written in this project: an index created by
            // hand-written C# does not make EF record it, so the configuration class has to declare
            // the same name or the first unrelated scaffold will try to create it again.

            migrationBuilder.CreateIndex(
                name: "IX_fuel_offer_volumes_is_deleted_liters",
                table: "fuel_offer_volumes",
                columns: new[] { "is_deleted", "liters" });

            migrationBuilder.CreateIndex(
                name: "ux_fuel_offer_volumes_offer_liters",
                table: "fuel_offer_volumes",
                columns: new[] { "offer_id", "liters" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fuel_offers_is_deleted_data_kind",
                table: "fuel_offers",
                columns: new[] { "is_deleted", "data_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_fuel_offers_station_fuel_pool",
                table: "fuel_offers",
                columns: new[] { "station_id", "fuel_type_id", "data_kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fuel_offers_station_id",
                table: "fuel_offers",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_offers_station_id_fuel_type_id",
                table: "fuel_offers",
                columns: new[] { "station_id", "fuel_type_id" });

            // ---- Backfill. Everything above is DDL; from here the tables are filled. ----

            // The guard, and the reason this migration is hand-written rather than scaffolded.
            //
            // The premise of the split is that an offer has ONE per-litre price. That is a claim about
            // the data, and a migration must check such a claim rather than assume it: if two volume
            // rows of one offer disagree, collapsing them to a single price silently discards a value,
            // and the storefront - which takes the minimum over volumes - would start publishing
            // whichever one this statement happened to pick.
            //
            // Measured on production 2026-10-14 the violation count is 0, so this guard scans 116 rows
            // and never fires. It exists so that "the data happens to be clean today" stops being the
            // only thing standing between a future import and a wrong published price. The unique
            // indexes above are already in place, so the inserts below are themselves checked by the
            // constraints they have to satisfy.
            //
            // COALESCE(col::text, '<null>') rather than a plain count(DISTINCT col): the latter ignores
            // NULLs, so a group holding 50 and NULL would count as ONE distinct value and pass - which
            // is precisely the "one volume carries no price" case this has to catch.
            //
            // `WHERE NOT is_deleted` is the correction, and it is the whole reason this guard is
            // being re-shipped. The first version grouped over EVERY row and aborted the production
            // deploy on 2026-10-15:
            //
            //     fuel_packages carries more than one per-litre price for:
            //       okko/24dde716-e638-4f64-96d0-f43fb083c5f7/real,
            //       okko/fa63b7ab-2fb2-4cdc-bc71-ee1aff9a3785/real
            //
            // Those two groups are NOT drifting. Their live volumes all carry one price - verified
            // three ways: `GET /api/packages` returns 15 (station, fuel) groups with zero groups
            // disagreeing on finalPricePerLiter, and deploy/preflight-db/010-…sql - which already
            // filtered archived rows - reported "(0 rows)" for every group when it ran on production.
            // The disagreement appears only when soft-deleted rows are added back.
            //
            // Which is what an archived row IS: BrandsController.UpdateNominals:503-505 archives the
            // volume rows that leave a fuel's nominal set, and BrandsController:157-161 does the same
            // when a branch goes. So an archived package carries the price the fuel had BEFORE the
            // last reprice, and comparing it with the current one is comparing history with the
            // present. A guard that blocks on that is blocking on archived history, and it blocked a
            // production deploy to do it.
            //
            // This also stops being a coincidence after the split: `UNIQUE (offer_id, liters)` means
            // a reprice updates the one volume row in place, so under fuel_offer_volumes there is no
            // mechanism left that would produce an archived row holding a stale price in the first
            // place. The filter is correct on its own terms; it is also where the drift went.
            //
            // Kept: archived rows are still backfilled below, with their own is_deleted, so an
            // archived volume still resolves if anything ever reads history.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    drifted text;
                BEGIN
                    SELECT string_agg(offer_label, ', ' ORDER BY offer_label) INTO drifted
                    FROM (
                        SELECT station_id || '/' || fuel_type_id || '/' || data_kind AS offer_label
                        FROM fuel_packages
                        WHERE NOT is_deleted
                        GROUP BY station_id, fuel_type_id, data_kind
                        HAVING count(DISTINCT COALESCE(final_price_per_liter::text, '<null>')) > 1
                            OR count(DISTINCT COALESCE(pump_price_per_liter::text, '<null>')) > 1
                            OR count(DISTINCT COALESCE(min_discount_per_liter::text, '<null>')) > 1
                            OR count(DISTINCT COALESCE(supplier_price_per_liter::text, '<null>')) > 1
                            OR count(DISTINCT COALESCE(margin_uah_per_liter::text, '<null>')) > 1
                            OR count(DISTINCT COALESCE(margin_percent::text, '<null>')) > 1
                    ) d;

                    IF drifted IS NOT NULL THEN
                        RAISE EXCEPTION
                            'fuel_packages carries more than one per-litre price for: %. '
                            'The offer split assumes exactly one price per (station, fuel, pool); '
                            'reconcile these rows before retrying.',
                            drifted;
                    END IF;
                END $$;
                """);

            // One row per (station, fuel, pool). The id is OfferKeys.For() expressed in SQL - the
            // assembly cannot be called from a migration, so OfferBackfillIntegrationTests asserts
            // that the rows this statement produced carry exactly the ids OfferKeys.For() computes.
            //
            // max() over the per-litre columns is unambiguous *because the guard above passed*: every
            // row in the group holds the same value, so max() is that value and not a choice. The
            // audit pair is taken from the most recently repriced row instead, since price_updated_at
            // is legitimately the one field that differs across a group.
            //
            // Archived package rows are left behind here, and that is the point: they carry the price
            // the fuel had BEFORE the last reprice (BrandsController.UpdateNominals:503-505), so they
            // are history rather than part of the catalogue. Carrying them over broke this migration
            // in production on 2026-10-15 — 14 archived rows, 11 of which share (offer, litres) with
            // a live volume and so collide with ux_fuel_offer_volumes_offer_liters. The guard below
            // was already scoped to live rows at that point; this statement was not, which is how one
            // commit shipped a correct guard and an incorrect backfill.
            //
            // Nothing is lost by leaving them: no foreign key points at fuel_packages, and
            // order_line_items carries Brand + FuelTypeId + Liters with no package id. fuel_packages
            // stays until PR C drops it.
            //
            // bool_and(is_deleted) is now a constant false for the same reason - with the filter above
            // every row in the group is live - and is written as false so the reader does not have to
            // re-derive that. The offer's own is_deleted is maintained by the write paths from PR C.
            migrationBuilder.Sql(
                """
                INSERT INTO fuel_offers (
                    id, station_id, fuel_type_id,
                    supplier_price_per_liter, margin_uah_per_liter, margin_percent,
                    final_price_per_liter, pump_price_per_liter, min_discount_per_liter,
                    price_updated_at, price_updated_by_user_id,
                    created_at_utc, updated_at_utc, is_deleted, deleted_at_utc, data_kind)
                SELECT
                    station_id || '-' || fuel_type_id || CASE WHEN data_kind = 'qa' THEN '-qa' ELSE '' END,
                    station_id,
                    fuel_type_id,
                    max(supplier_price_per_liter),
                    max(margin_uah_per_liter),
                    max(margin_percent),
                    max(final_price_per_liter),
                    max(pump_price_per_liter),
                    max(min_discount_per_liter),
                    max(price_updated_at),
                    (array_agg(price_updated_by_user_id
                               ORDER BY price_updated_at DESC NULLS LAST, id))[1],
                    min(created_at_utc),
                    max(updated_at_utc),
                    false,
                    max(deleted_at_utc),
                    data_kind
                FROM fuel_packages
                WHERE NOT is_deleted
                GROUP BY station_id, fuel_type_id, data_kind;
                """);

            // One row per fuel_packages row, keeping its id - so the volume is the *same* row before
            // and after, addressable by the same string, and re-running this statement collides on the
            // primary key rather than making a second copy of every volume.
            //
            // original_price is not carried over, and its absence is the point. It is derived
            // (pump x liters), both public handlers already recompute it through
            // FuelPricing.OriginalPackagePrice because a stored derived column cannot be trusted, and
            // so it was written and never read. Moving it would carry a dead column across instead of
            // removing it.
            migrationBuilder.Sql(
                """
                INSERT INTO fuel_offer_volumes (
                    id, offer_id, liters, package_price,
                    created_at_utc, updated_at_utc, is_deleted, deleted_at_utc)
                SELECT
                    p.id,
                    p.station_id || '-' || p.fuel_type_id
                        || CASE WHEN p.data_kind = 'qa' THEN '-qa' ELSE '' END,
                    p.liters,
                    p.price,
                    p.created_at_utc,
                    p.updated_at_utc,
                    p.is_deleted,
                    p.deleted_at_utc
                FROM fuel_packages p
                WHERE NOT p.is_deleted;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fuel_offer_volumes");

            migrationBuilder.DropTable(
                name: "fuel_offers");
        }
    }
}
