using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class SettlementMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "settlement_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    telegram_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    amount_uah = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    resolved_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settlement_messages", x => x.id);
                    table.CheckConstraint("ck_settlement_messages_amount_positive", "amount_uah IS NULL OR amount_uah > 0");
                    table.CheckConstraint("ck_settlement_messages_outcome", "outcome IN ('accepted','unallocated','unparseable','rejected_sender','rejected_chat')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_settlement_messages_refusals",
                table: "settlement_messages",
                column: "sent_at_utc",
                filter: "outcome <> 'accepted'");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_messages_sender_time",
                table: "settlement_messages",
                columns: new[] { "telegram_user_id", "sent_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_settlement_messages_chat_message",
                table: "settlement_messages",
                columns: new[] { "chat_id", "message_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settlement_messages");
        }
    }
}
