using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />

/// <summary>
/// Moves the term ladders out of app_settings key/value and into a dated table.
/// </summary>
/// <remarks>
/// <para>
/// The two ladders - an absolute renewal price per litre, and a purchase-term discount per litre -
/// were 32 unversioned string keys. Changing one overwrote the only record of what it had been, so
/// "what did a renewal cost on 13 March" was unanswerable and an operator mistake was invisible.
/// </para>
/// <para>
/// This is a lookup, not a rule table, and deliberately so: every mainstream pricing system keeps
/// its base ladder in a catalogue of tiers and reserves rules for adjustments on top. A ladder needs
/// a term and an on/off flag, and neither is a rule target or a single number - which is why
/// <c>pricing_rules</c> cannot hold this.
/// </para>
/// <para>
/// The app_settings rows are left in place. They become the fallback the loader uses when a tier
/// has no row here, so deploy order cannot change a price, and Down is a plain drop.
/// </para>
/// </remarks>
    public partial class VoucherTermPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "voucher_term_pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheme = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    term_code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    rate_per_liter = table.Column<decimal>(type: "numeric(14,4)", nullable: false),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voucher_term_pricing", x => x.id);
                    table.CheckConstraint("ck_voucher_term_pricing_rate_non_negative", "rate_per_liter >= 0");
                    table.CheckConstraint("ck_voucher_term_pricing_scheme", "scheme IN ('renewal_rate','purchase_term_discount')");
                    table.CheckConstraint("ck_voucher_term_pricing_term_code", "term_code IN ('1w','2w','1m','2m','3m','4m','5m','6m')");
                    table.CheckConstraint("ck_voucher_term_pricing_valid_window", "valid_to IS NULL OR valid_to > valid_from");
                });

            migrationBuilder.CreateIndex(
                name: "ix_voucher_term_pricing_current",
                table: "voucher_term_pricing",
                columns: new[] { "scheme", "valid_from", "valid_to" });

            migrationBuilder.CreateIndex(
                name: "ix_voucher_term_pricing_updated_by",
                table: "voucher_term_pricing",
                column: "updated_by_user_id",
                filter: "updated_by_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_voucher_term_pricing_open_tier",
                table: "voucher_term_pricing",
                columns: new[] { "scheme", "term_code" },
                unique: true,
                filter: "valid_to IS NULL");

            // Carry the live ladder across. The 32 app_settings rows are the only record of what
            // these tiers cost today - overwriting one of them in the old design destroyed the
            // previous value, so there is no history to migrate and nothing else to copy from. They
            // become the current (valid_to IS NULL) row per tier, dated now.
            //
            // Idempotent per house style: the WHERE NOT EXISTS guard means re-running is a no-op,
            // and it also means a tier whose settings row was never saved still arrives here -
            // disabled with a zero rate, which is what the loader already did for a missing key.
            //
            // Every cast is guarded. The old keys were strings written by .ToString(), but a value
            // edited by hand in psql could be anything, and an unparseable cast aborts a migration
            // at deploy time - which is a crash-loop, not a fallback.
            migrationBuilder.Sql(
                """
                INSERT INTO voucher_term_pricing
                    (id, scheme, term_code, enabled, rate_per_liter, valid_from, valid_to, created_at_utc)
                SELECT
                    gen_random_uuid(),
                    'renewal_rate',
                    t.term_code,
                    COALESCE((
                        SELECT CASE WHEN s.value ~* '^true$' THEN true ELSE false END
                        FROM app_settings s
                        WHERE s.key = 'VoucherRenewal:Tier:' || t.term_code || ':Enabled'), false),
                    GREATEST(COALESCE((
                        SELECT CASE WHEN s.value ~ '^-?[0-9]+(\.[0-9]+)?$' THEN s.value::numeric ELSE 0 END
                        FROM app_settings s
                        WHERE s.key = 'VoucherRenewal:Tier:' || t.term_code || ':RatePerLiter'), 0), 0),
                    now(), NULL, now()
                FROM unnest(ARRAY['1w','2w','1m','2m','3m','4m','5m','6m']) AS t(term_code)
                WHERE NOT EXISTS (
                    SELECT 1 FROM voucher_term_pricing v
                    WHERE v.scheme = 'renewal_rate' AND v.term_code = t.term_code AND v.valid_to IS NULL);

                INSERT INTO voucher_term_pricing
                    (id, scheme, term_code, enabled, rate_per_liter, valid_from, valid_to, created_at_utc)
                SELECT
                    gen_random_uuid(),
                    'purchase_term_discount',
                    t.term_code,
                    COALESCE((
                        SELECT CASE WHEN s.value ~* '^true$' THEN true ELSE false END
                        FROM app_settings s
                        WHERE s.key = 'VoucherTerm:Tier:' || t.term_code || ':Enabled'), false),
                    GREATEST(COALESCE((
                        SELECT CASE WHEN s.value ~ '^-?[0-9]+(\.[0-9]+)?$' THEN s.value::numeric ELSE 0 END
                        FROM app_settings s
                        WHERE s.key = 'VoucherTerm:Tier:' || t.term_code || ':DiscountPerLiter'), 0), 0),
                    now(), NULL, now()
                FROM unnest(ARRAY['1w','2w','1m','2m','3m','4m','5m','6m']) AS t(term_code)
                WHERE NOT EXISTS (
                    SELECT 1 FROM voucher_term_pricing v
                    WHERE v.scheme = 'purchase_term_discount' AND v.term_code = t.term_code AND v.valid_to IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "voucher_term_pricing");
        }
    }
}
