using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

/// <summary>
/// Drops <c>pricing_rules</c> - the D-09 rule table, which nothing ever read and whose job is
/// already done elsewhere.
/// </summary>
/// <remarks>
/// <para>
/// It shipped on 2026-10-18 in <c>20261018133552_PricingRules</c> and was superseded by the end of
/// that same day, which is why it is dropped a day later rather than never built.
/// </para>
/// <para>
/// <b>Why nothing reads it.</b> It was designed to hold pricing configuration as effective-dated,
/// approval-gated rules. Two later changes took that job apart and moved each piece to where it
/// belongs:
/// </para>
/// <para>
/// - <c>profit_per_liter</c> became <c>fuel_types.margin_uah_per_liter</c> when the offer's copy of
/// the margin was removed on 2026-10-17. Fifteen of twenty-three fuels carry it today.
/// - <c>term_discount_per_liter</c> became <c>voucher_term_pricing</c> on 2026-10-18. It could not
/// have gone here anyway: a ladder needs a term and an on/off flag, and a rule has one target and
/// one number.
/// - <c>min_discount_per_liter</c> is <c>fuel_offers.min_discount_per_liter</c>, which is the pump
/// gap and is not a rule.
/// </para>
/// <para>
/// <c>discount_percent</c> and <c>fixed_price_per_liter</c> never held a row and have no home
/// anywhere; <c>fuel_offers.margin_percent</c> is likewise empty.
/// </para>
/// <para>
/// <b>Why this is safe.</b> The table was empty when this ran: zero rows, zero readers, zero inbound
/// foreign keys. <c>Down</c> recreates it in full, and <c>20261018133552_PricingRules</c> is
/// untouched - the history is append-only, so the table is dropped by a later migration rather than
/// by editing one production has already applied.
/// </para>
/// </remarks>
namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class DropInertPricingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pricing_rules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pricing_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    offer_id = table.Column<string>(type: "text", maxLength: 200, nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    rule_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "draft"),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    value = table.Column<decimal>(type: "numeric(14,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pricing_rules", x => x.id);
                    table.CheckConstraint("ck_pricing_rules_approved_has_approver", "(status = 'approved') = (approved_by_user_id IS NOT NULL)");
                    table.CheckConstraint("ck_pricing_rules_exactly_one_scope_target", "    (scope = 'global'        AND offer_id IS NULL AND legal_entity_id IS NULL AND contract_id IS NULL)\n OR (scope = 'offer'         AND offer_id IS NOT NULL AND legal_entity_id IS NULL AND contract_id IS NULL)\n OR (scope = 'legal_entity'  AND offer_id IS NULL AND legal_entity_id IS NOT NULL AND contract_id IS NULL)\n OR (scope = 'contract'      AND offer_id IS NULL AND legal_entity_id IS NULL AND contract_id IS NOT NULL)");
                    table.CheckConstraint("ck_pricing_rules_kind", "rule_kind IN ('profit_per_liter','min_discount_per_liter','discount_percent','fixed_price_per_liter','term_discount_per_liter')");
                    table.CheckConstraint("ck_pricing_rules_scope", "scope IN ('offer','legal_entity','contract','global')");
                    table.CheckConstraint("ck_pricing_rules_status", "status IN ('draft','pending_approval','approved','rejected','superseded')");
                    table.CheckConstraint("ck_pricing_rules_valid_window", "valid_to IS NULL OR valid_to > valid_from");
                    table.CheckConstraint("ck_pricing_rules_value_non_negative", "value >= 0");
                    table.ForeignKey(
                        name: "FK_pricing_rules_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pricing_rules_fuel_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "fuel_offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pricing_rules_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pricing_rules_users_approved_by_user_id",
                        column: x => x.approved_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pricing_rules_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pricing_rules_active",
                table: "pricing_rules",
                columns: new[] { "scope", "offer_id", "legal_entity_id", "rule_kind", "valid_from" },
                descending: new[] { false, false, false, false, true },
                filter: "status = 'approved'");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_rules_approved_by_user_id",
                table: "pricing_rules",
                column: "approved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_rules_contract_id",
                table: "pricing_rules",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_rules_legal_entity_id",
                table: "pricing_rules",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_rules_offer_id",
                table: "pricing_rules",
                column: "offer_id");

            migrationBuilder.CreateIndex(
                name: "ix_pricing_rules_requester",
                table: "pricing_rules",
                columns: new[] { "requested_by_user_id", "requested_at_utc" });
        }
    }
}
