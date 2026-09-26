using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPushTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "push_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    device_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_push_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_push_tokens_token",
                table: "push_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_push_tokens_user_id",
                table: "push_tokens",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_tokens");
        }
    }
}
