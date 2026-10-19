using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Renames <c>fuel_offer_prices</c> to <c>fuel_price_history</c>, and every constraint and index
    /// that carries the old name with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why.</b> The offer is gone. <c>fuel_offers</c> was dropped on 2026-10-19
    /// (<c>20261019095748_OfferPricingBecomesTheFuel</c>) and the price moved onto the fuel, so the
    /// history table was left with a name that points at a table nobody has. A name that names
    /// something that no longer exists is not a small thing in a schema: it sends the next reader
    /// looking for an offer that is not there, and it keeps the whole feature area - the entity, the
    /// recorder, the reasons - speaking a vocabulary the database has already moved on from.
    /// </para>
    /// <para>
    /// <b>What the table is.</b> The append-only record of what each fuel was priced at, and when.
    /// It is read by <c>GET /api/admin/brands/fuels/{fuelId}/price-history</c> and by the
    /// price-at query, and written by the one choke point every price write path calls.
    /// </para>
    /// <para>
    /// <b>Why this is a rename and not a drop and a create.</b> The scaffolded migration this file
    /// replaces dropped the table and created it again under the new name - which would have taken
    /// every recorded price with it. The history is the only record of what a fuel cost on the day an
    /// order was paid; a rename that empties it is a rename that loses money history, so every
    /// statement below is a rename and nothing else. The rows, their ids and their order are
    /// untouched, and the same is true of the Down.
    /// </para>
    /// <para>
    /// <b>Why the constraints and the indexes are renamed too.</b> A check constraint, a foreign key
    /// and an index that still carry the old table's name are the same lie in a different place: the
    /// name is what an operator searching for a rule finds, and <c>ck_fuel_offer_prices_reason</c>
    /// answers to no table. The names are part of the schema's contract with the operator, so they
    /// move with the table. The foreign keys are the sharpest case - EF names them after the table
    /// they hang off, so a table renamed without them would leave two constraints pointing at a
    /// relation that no longer has that name.
    /// </para>
    /// <para>
    /// <b>Why this migration is the last one in the chain.</b> The foreign keys were added on
    /// 2026-10-19 by <c>20261019133221_CatalogueCarriesItsForeignKeys</c>, which names the table it
    /// alters. A rename that ran before it would leave that migration pointing at a relation that
    /// has already been renamed, and the deploy would die on it. So the rename comes after.
    /// </para>
    /// </remarks>
    public partial class RenameFuelOfferPricesToFuelPriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The table. Everything below renames what belongs to it, so it goes first: the other
            // statements name the new table.
            migrationBuilder.Sql("""ALTER TABLE "fuel_offer_prices" RENAME TO "fuel_price_history";""");

            // The primary key, which is a constraint and not an index in Postgres - it is renamed with
            // the constraint syntax, not the index one.
            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "PK_fuel_offer_prices" TO "PK_fuel_price_history";""");

            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "ck_fuel_offer_prices_data_kind" TO "ck_fuel_price_history_data_kind";""");
            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "ck_fuel_offer_prices_fuel_type_id_length" TO "ck_fuel_price_history_fuel_type_id_length";""");
            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "ck_fuel_offer_prices_margin_signs" TO "ck_fuel_price_history_margin_signs";""");
            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "ck_fuel_offer_prices_prices_non_negative" TO "ck_fuel_price_history_prices_non_negative";""");
            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "ck_fuel_offer_prices_reason" TO "ck_fuel_price_history_reason";""");

            // The two foreign keys. EF names them after the table, so a table renamed without them
            // would leave two constraints pointing at a relation that no longer carries that name.
            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "FK_fuel_offer_prices_brands_brand_id" TO "FK_fuel_price_history_brands_brand_id";""");
            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "FK_fuel_offer_prices_fuel_types_fuel_type_id" TO "FK_fuel_price_history_fuel_types_fuel_type_id";""");

            // The three indexes: the one on brand_id, the one EF named for itself on
            // changed_by_user_id, and the one the model names outright on (fuel, effective_from).
            migrationBuilder.Sql("""ALTER INDEX "ix_fuel_offer_prices_brand_id" RENAME TO "ix_fuel_price_history_brand_id";""");
            migrationBuilder.Sql("""ALTER INDEX "ix_fuel_offer_prices_changed_by_user_id" RENAME TO "ix_fuel_price_history_changed_by_user_id";""");
            migrationBuilder.Sql("""ALTER INDEX "ix_fuel_offer_prices_fuel_effective" RENAME TO "ix_fuel_price_history_fuel_effective";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The reverse, in the reverse order: the indexes and the constraints are put back under
            // their old names first, because they are named after the table, and the table is renamed
            // last - a constraint can only be renamed on the table it belongs to.
            migrationBuilder.Sql("""ALTER INDEX "ix_fuel_price_history_fuel_effective" RENAME TO "ix_fuel_offer_prices_fuel_effective";""");
            migrationBuilder.Sql("""ALTER INDEX "ix_fuel_price_history_changed_by_user_id" RENAME TO "ix_fuel_offer_prices_changed_by_user_id";""");
            migrationBuilder.Sql("""ALTER INDEX "ix_fuel_price_history_brand_id" RENAME TO "ix_fuel_offer_prices_brand_id";""");

            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "FK_fuel_price_history_fuel_types_fuel_type_id" TO "FK_fuel_offer_prices_fuel_types_fuel_type_id";""");
            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "FK_fuel_price_history_brands_brand_id" TO "FK_fuel_offer_prices_brands_brand_id";""");

            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "ck_fuel_price_history_reason" TO "ck_fuel_offer_prices_reason";""");
            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "ck_fuel_price_history_prices_non_negative" TO "ck_fuel_offer_prices_prices_non_negative";""");
            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "ck_fuel_price_history_margin_signs" TO "ck_fuel_offer_prices_margin_signs";""");
            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "ck_fuel_price_history_fuel_type_id_length" TO "ck_fuel_offer_prices_fuel_type_id_length";""");
            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "ck_fuel_price_history_data_kind" TO "ck_fuel_offer_prices_data_kind";""");

            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME CONSTRAINT "PK_fuel_price_history" TO "PK_fuel_offer_prices";""");

            migrationBuilder.Sql("""ALTER TABLE "fuel_price_history" RENAME TO "fuel_offer_prices";""");
        }
    }
}
