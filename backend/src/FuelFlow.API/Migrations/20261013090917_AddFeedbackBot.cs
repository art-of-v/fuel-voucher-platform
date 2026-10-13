using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackBot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feedback_chat_bindings",
                columns: table => new
                {
                    telegram_chat_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bound_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback_chat_bindings", x => x.telegram_chat_id);
                });

            migrationBuilder.CreateTable(
                name: "feedback_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    telegram_chat_id = table.Column<long>(type: "bigint", nullable: false),
                    telegram_message_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    verdict = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    severity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    confidence = table.Column<double>(type: "double precision", precision: 3, scale: 2, nullable: true),
                    classifier_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    classified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    staff_chat_id = table.Column<long>(type: "bigint", nullable: true),
                    staff_message_id = table.Column<long>(type: "bigint", nullable: true),
                    staff_replied_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    app_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback_messages", x => x.id);
                    table.CheckConstraint("ck_feedback_message_category", "category IS NULL OR category IN ('Bug', 'Idea', 'Complaint', 'Ux', 'Question', 'Praise', 'Other')");
                    table.CheckConstraint("ck_feedback_message_language", "language IS NULL OR language IN ('Uk', 'Ru', 'En', 'Other')");
                    table.CheckConstraint("ck_feedback_message_severity", "severity IS NULL OR severity IN ('Low', 'Medium', 'High')");
                    table.CheckConstraint("ck_feedback_message_status", "status IN ('New', 'Triaged', 'Handled', 'Rejected')");
                    table.CheckConstraint("ck_feedback_message_verdict", "verdict IS NULL OR verdict IN ('Junk', 'Kept')");
                });

            migrationBuilder.CreateTable(
                name: "feedback_message_replies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feedback_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_user_id = table.Column<long>(type: "bigint", nullable: false),
                    staff_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    delivered_message_id = table.Column<long>(type: "bigint", nullable: true),
                    delivery_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback_message_replies", x => x.id);
                    table.ForeignKey(
                        name: "FK_feedback_message_replies_feedback_messages_feedback_message~",
                        column: x => x.feedback_message_id,
                        principalTable: "feedback_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_feedback_chat_bindings_user_id",
                table: "feedback_chat_bindings",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_message_replies_feedback_message_id",
                table: "feedback_message_replies",
                column: "feedback_message_id");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_message_replies_sent_at_utc",
                table: "feedback_message_replies",
                column: "sent_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_messages_category",
                table: "feedback_messages",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_messages_created_at_utc",
                table: "feedback_messages",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_messages_status",
                table: "feedback_messages",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_messages_telegram_chat_id_telegram_message_id",
                table: "feedback_messages",
                columns: new[] { "telegram_chat_id", "telegram_message_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feedback_chat_bindings");

            migrationBuilder.DropTable(
                name: "feedback_message_replies");

            migrationBuilder.DropTable(
                name: "feedback_messages");
        }
    }
}
