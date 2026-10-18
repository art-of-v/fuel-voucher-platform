using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class PricingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pricing_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    offer_id = table.Column<string>(type: "text", maxLength: 200, nullable: true),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    value = table.Column<decimal>(type: "numeric(14,4)", nullable: false),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "draft"),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pricing_rules");
        }
    }
}
