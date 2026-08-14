using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    worker_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_invitations", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_invitations_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_company_invitations_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_company_invitations_users_worker_user_id",
                        column: x => x.worker_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_members", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_members_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_company_members_users_worker_user_id",
                        column: x => x.worker_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_invitations_legal_entity_id",
                table: "company_invitations",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_invitations_owner_user_id",
                table: "company_invitations",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_invitations_worker_user_id",
                table: "company_invitations",
                column: "worker_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_invitations_worker_user_id_status",
                table: "company_invitations",
                columns: new[] { "worker_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_company_members_legal_entity_id",
                table: "company_members",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_members_worker_user_id",
                table: "company_members",
                column: "worker_user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_invitations");

            migrationBuilder.DropTable(
                name: "company_members");
        }
    }
}
