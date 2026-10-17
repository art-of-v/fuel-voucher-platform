using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// D-01 - the operator's own legal entity, and its bank accounts.
    /// <para>
    /// Until this exists every fiscal document has no issuer: no EDRPOU of our own, no tax regime,
    /// no bank account, no name to print on an invoice. The moment a delivery note or a fiscal
    /// receipt is issued (#98, #119, #120) the document has nowhere to name an issuer from. That is
    /// a schema gap, not a missing feature, and it is the cheapest item on the money roadmap: one
    /// table, one foreign-key target for every document that follows.
    /// </para>
    /// <para>
    /// Two things this migration deliberately does not do. It adds no <c>tenant_id</c>: the owner
    /// confirmed single-operator, and multi-tenancy would add a key to roughly thirty tables to
    /// solve a problem that does not exist. And it seeds no row - the operator's real EDRPOU and VAT
    /// number are business data, and a guessed identifier on a fiscal document is worse than no
    /// identifier. The table ships empty and inactive; someone fills it in and activates it.
    /// </para>
    /// </summary>
    public partial class OperatorLegalEntityAndBankAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operator_legal_entities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    edrpou = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    legal_form = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    vat_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    rnkrr = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tax_regime = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    registered_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operator_legal_entities", x => x.id);
                    table.CheckConstraint("ck_operator_legal_entities_edrpou_digits", "edrpou ~ '^[0-9]{8,10}$'");
                    table.CheckConstraint("ck_operator_legal_entities_edrpou_not_blank", "btrim(edrpou) <> ''");
                    table.CheckConstraint("ck_operator_legal_entities_name_not_blank", "btrim(name) <> ''");
                    table.CheckConstraint("ck_operator_legal_entities_rnkrr_digits", "rnkrr IS NULL OR rnkrr ~ '^[0-9]{5}$'");
                    table.CheckConstraint("ck_operator_legal_entities_tax_regime", "tax_regime IN ('Vat','Simplified')");
                    table.CheckConstraint("ck_operator_legal_entities_vat_number_digits", "vat_number IS NULL OR vat_number ~ '^[0-9]{12}$'");
                });

            migrationBuilder.CreateTable(
                name: "bank_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_accounts", x => x.id);
                    table.CheckConstraint("ck_bank_accounts_account_ref_length", "length(account_ref) <= 100");
                    table.CheckConstraint("ck_bank_accounts_account_ref_not_blank", "btrim(account_ref) <> ''");
                    table.CheckConstraint("ck_bank_accounts_bank_code_length", "length(bank_code) <= 20");
                    table.CheckConstraint("ck_bank_accounts_bank_code_not_blank", "btrim(bank_code) <> ''");
                    table.CheckConstraint("ck_bank_accounts_currency", "currency = 'UAH'");
                    table.ForeignKey(
                        name: "FK_bank_accounts_operator_legal_entities_operator_legal_entity~",
                        column: x => x.operator_legal_entity_id,
                        principalTable: "operator_legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Removed from the generated migration, deliberately:
            //
            //   AddCheckConstraint ck_voucher_movements_actor_is_somebody
            //   AddCheckConstraint ck_voucher_events_actor_is_somebody
            //
            // `migrations add` emitted both although the model, the snapshot and the Designer
            // snapshot all already carry those two constraints with identical text, and
            // 20261017152948_ActorIdMustNameSomebody already creates them. Re-creating them aborts a
            // fresh migrate with 42710 "constraint already exists".
            //
            // CORRECTION, 2026-10-17: an earlier version of this comment said the cause was not
            // established and that "the next person to add a migration will meet it again". That was
            // wrong, and it was wrong in a way that would have sent them looking for a phantom.
            // The cause was `migrations remove --force` run against an unreachable database, which
            // reverted the snapshot from a state it should not have been reverted from.
            // 20261017175323_PartySupertypeAdditive, generated normally on the next attempt, has
            // neither operation. A normal `migrations add` does not produce them.
            //
            // What stays true: do not read a redundant constraint operation as harmless, and re-run
            // the suite after touching a migration by hand.

            migrationBuilder.CreateIndex(
                name: "IX_bank_accounts_operator_legal_entity_id",
                table: "bank_accounts",
                column: "operator_legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_accounts_operator_legal_entity_id_bank_code_account_ref",
                table: "bank_accounts",
                columns: new[] { "operator_legal_entity_id", "bank_code", "account_ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operator_legal_entities_edrpou",
                table: "operator_legal_entities",
                column: "edrpou",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_operator_legal_entities_single_active",
                table: "operator_legal_entities",
                column: "is_active",
                unique: true,
                filter: "is_active");

            // At most one default settlement account per entity, so a document that does not name an
            // account has exactly one to name. Raw SQL because EF identifies an index by its property
            // list: a second HasIndex on operator_legal_entity_id would have reconfigured
            // IX_bank_accounts_operator_legal_entity_id instead of creating a second index, silently
            // turning the foreign-key lookup into this filtered one.
            //
            // Nothing in the EF model knows this index exists, so no model-based test can see it.
            // OperatorLegalEntitySchemaTests asserts it against the live database instead - the same
            // silence CheckConstraintColumnDriftTests was written about, one level up.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_bank_accounts_single_default_per_entity
                    ON bank_accounts (operator_legal_entity_id)
                    WHERE is_default;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_accounts");

            migrationBuilder.DropTable(
                name: "operator_legal_entities");

            // No counterpart to the two DropCheckConstraint operations EF generated: they belonged to
            // constraints this migration never created. Rolling back must not remove a constraint that
            // 20261017152948_ActorIdMustNameSomebody owns.

            // The raw-SQL index above goes with the table it was defined on.
        }
    }
}
