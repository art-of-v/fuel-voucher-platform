using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class ContractsGetTheirTextAndTheirWriter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "terms_version",
                table: "user_contracts",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1,
                comment: "The version of the text that was actually signed. The contract's own version is what it points at NOW; this is what the customer read at the moment they signed, and the two can differ.");

            migrationBuilder.AddColumn<short>(
                name: "terms_version",
                table: "contracts",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1,
                comment: "The version of the provider's text this contract is against - the one the customer reads and signs. A contract that says 'version 2' while the provider's text is at version 3 is a question the act and the audit trail have to be able to answer.");

            migrationBuilder.CreateTable(
                name: "contract_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The provider whose text this is. One text per provider - the terms of one network, not of the platform."),
                    version = table.Column<short>(type: "smallint", nullable: false, comment: "The version number, counting up from 1 within the provider. A new version never overwrites the old one - what a customer signed is a question the audit trail has to answer."),
                    body = table.Column<string>(type: "text", nullable: false, comment: "The text itself - what the customer reads before signing. This is the whole point of the row: before it, the screen's REVIEW THE TEXT had nothing behind it."),
                    term_days = table.Column<int>(type: "integer", nullable: false, comment: "How long a contract made against this version is valid, in days. The term belongs to the text: a new version of the terms is a different agreement with a different term."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "When this version was published."),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Who published it. NULL for a version that was seeded rather than written by somebody.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_versions", x => x.id);
                    table.CheckConstraint("ck_contract_versions_body_not_blank", "btrim(body) <> ''");
                    table.CheckConstraint("ck_contract_versions_term_positive", "term_days > 0");
                    table.CheckConstraint("ck_contract_versions_version_positive", "version > 0");
                    table.ForeignKey(
                        name: "FK_contract_versions_brands_brand_id",
                        column: x => x.brand_id,
                        principalTable: "brands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contract_versions_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_contracts_terms_version_positive",
                table: "user_contracts",
                sql: "terms_version > 0");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_legal_entity_id_brand_id",
                table: "contracts",
                columns: new[] { "legal_entity_id", "brand_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_terms_version_positive",
                table: "contracts",
                sql: "terms_version > 0");

            migrationBuilder.CreateIndex(
                name: "ix_contract_versions_brand_id",
                table: "contract_versions",
                column: "brand_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_versions_brand_id_version",
                table: "contract_versions",
                columns: new[] { "brand_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contract_versions_created_by_user_id",
                table: "contract_versions",
                column: "created_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contract_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_contracts_terms_version_positive",
                table: "user_contracts");

            migrationBuilder.DropIndex(
                name: "ix_contracts_legal_entity_id_brand_id",
                table: "contracts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_terms_version_positive",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "terms_version",
                table: "user_contracts");

            migrationBuilder.DropColumn(
                name: "terms_version",
                table: "contracts");
        }
    }
}
