using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Adds <c>fuel_offer_prices</c>: the append-only record of what each offer was priced at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why.</b> A price lived in one place - the offer's own columns - and was overwritten in place.
    /// <c>price_updated_at</c> said when it last moved and <c>price_updated_by_user_id</c> who moved
    /// it, and two of the five write paths stamped neither, so "what did this fuel cost last Tuesday"
    /// had no answer. Money that already moved cannot be re-derived from the present: the dashboard,
    /// reconciliation and the report multiply today's margin by historical order lines, so a margin
    /// change today silently restates yesterday's profit.
    /// </para>
    /// <para>
    /// <b>What the backfill says, and what it does not.</b> One row per offer, holding the prices as
    /// they stand today, with <c>effective_from_utc</c> taken from
    /// <c>coalesce(price_updated_at, updated_at_utc, created_at_utc)</c> - the best available
    /// statement of when these figures became current. That is an approximation and is recorded as one:
    /// the row is honest about the price and honest that the instant is inferred, rather than inventing
    /// a history nobody kept. From the moment this migration lands, every price change appends a row
    /// with its real effective time (<c>OfferPriceRecorder</c>).
    /// </para>
    /// <para>
    /// <b>Why the backfill covers archived offers too.</b> They carry prices, those prices were quoted,
    /// and the query filter on <c>fuel_offers</c> hides them from the catalogue without making their
    /// numbers untrue. Skipping them would leave a fuel whose offer was retired with no history at all.
    /// </para>
    /// <para>
    /// <b>RESTRICT, not CASCADE.</b> Deleting an offer is now blocked while its prices exist, which is
    /// the point: a price row is a record of something that was sold. The one script that removes a
    /// whole pool - <c>scripts/remove-qa-catalogue.sql</c> - deletes the history before the offers.
    /// </para>
    /// <para>
    /// <b>No unique index on (offer_id, effective_from).</b> Two reprices inside one transaction can
    /// share a timestamp, and refusing the second would trade a tie-break (the id, a version 7 UUID, is
    /// chronological) for a write that can fail. See <c>FuelOfferPrice</c>.
    /// </para>
    /// </remarks>
    public partial class AddFuelOfferPriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fuel_offer_prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<string>(type: "text", nullable: false),
                    station_id = table.Column<string>(type: "text", nullable: false),
                    fuel_type_id = table.Column<string>(type: "text", nullable: false),
                    supplier_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    margin_uah_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    margin_percent = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    final_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    pump_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    min_discount_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    data_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_offer_prices", x => x.id);
                    table.CheckConstraint("ck_fuel_offer_prices_data_kind", "data_kind IN ('real', 'qa')");
                    table.CheckConstraint("ck_fuel_offer_prices_fuel_type_id_length", "length(fuel_type_id) <= 100");
                    table.CheckConstraint("ck_fuel_offer_prices_margin_signs", "margin_uah_per_liter IS NULL OR margin_uah_per_liter >= 0");
                    table.CheckConstraint("ck_fuel_offer_prices_offer_id_length", "length(offer_id) <= 103");
                    table.CheckConstraint("ck_fuel_offer_prices_prices_non_negative", "(final_price_per_liter IS NULL OR final_price_per_liter >= 0) AND (pump_price_per_liter IS NULL OR pump_price_per_liter >= 0) AND (min_discount_per_liter IS NULL OR min_discount_per_liter >= 0)");
                    table.CheckConstraint("ck_fuel_offer_prices_reason", "reason IN ('operator', 'pump-import', 'catalogue-import', 'batch-cost', 'exchange-release', 'backfill')");
                    table.CheckConstraint("ck_fuel_offer_prices_station_id_length", "length(station_id) <= 100");
                    table.ForeignKey(
                        name: "FK_fuel_offer_prices_fuel_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "fuel_offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_offer_prices_changed_by_user_id",
                table: "fuel_offer_prices",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_fuel_offer_prices_fuel_effective",
                table: "fuel_offer_prices",
                columns: new[] { "fuel_type_id", "effective_from_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_fuel_offer_prices_offer_effective",
                table: "fuel_offer_prices",
                columns: new[] { "offer_id", "effective_from_utc" });

            // The opening row for every offer that exists. `gen_random_uuid` rather than a derived id
            // because the key of this row is the offer, not the row: nothing has referred to it yet and
            // nothing will - the history is read by (offer, time), never by id.
            //
            // Guarded by NOT EXISTS on the offer id rather than ON CONFLICT, because there is no unique
            // index to conflict against (see the remarks). Re-running the SQL by hand therefore writes
            // nothing, which is what a migration that is retried after a partial failure needs.
            migrationBuilder.Sql("""
                INSERT INTO fuel_offer_prices (
                    id, offer_id, station_id, fuel_type_id,
                    supplier_price_per_liter, margin_uah_per_liter, margin_percent,
                    final_price_per_liter, pump_price_per_liter, min_discount_per_liter,
                    data_kind, effective_from_utc, recorded_at_utc, changed_by_user_id, reason)
                SELECT
                    gen_random_uuid(),
                    o.id,
                    o.station_id,
                    o.fuel_type_id,
                    o.supplier_price_per_liter,
                    o.margin_uah_per_liter,
                    o.margin_percent,
                    o.final_price_per_liter,
                    o.pump_price_per_liter,
                    o.min_discount_per_liter,
                    o.data_kind,
                    coalesce(o.price_updated_at, o.updated_at_utc, o.created_at_utc, now()),
                    now(),
                    o.price_updated_by_user_id,
                    'backfill'
                FROM fuel_offers o
                WHERE NOT EXISTS (SELECT 1 FROM fuel_offer_prices p WHERE p.offer_id = o.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The history, not the current price: `fuel_offers` keeps its own price columns and nothing
            // here is read by the storefront, so dropping this table loses only the record of how the
            // present prices were arrived at. Stated rather than left for someone to discover.
            migrationBuilder.DropTable(
                name: "fuel_offer_prices");
        }
    }
}
