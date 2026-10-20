using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class MissingForeignKeysAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "changed_by_user_id",
                table: "provider_event_outbox",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // The all-zero guid stood for "no user" in a column that was NOT NULL. The column is
            // nullable now, so the rows that carried the sentinel say what they mean. The name the
            // human gave is kept in changed_by_user_name and is not touched here.
            migrationBuilder.Sql(
                """
                UPDATE "provider_event_outbox"
                SET "changed_by_user_id" = NULL
                WHERE "changed_by_user_id" = '00000000-0000-0000-0000-000000000000'::uuid;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_batches_fuel_type_id",
                table: "purchase_batches",
                column: "fuel_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_messages_user_id",
                table: "feedback_messages",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_feedback_chat_bindings_users_user_id",
                table: "feedback_chat_bindings",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_feedback_messages_users_user_id",
                table: "feedback_messages",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_order_line_items_fuel_types_fuel_type_id",
                table: "order_line_items",
                column: "fuel_type_id",
                principalTable: "fuel_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_batches_fuel_types_fuel_type_id",
                table: "purchase_batches",
                column: "fuel_type_id",
                principalTable: "fuel_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_users_parties_party_id",
                table: "users",
                column: "party_id",
                principalTable: "parties",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_feedback_chat_bindings_users_user_id",
                table: "feedback_chat_bindings");

            migrationBuilder.DropForeignKey(
                name: "FK_feedback_messages_users_user_id",
                table: "feedback_messages");

            migrationBuilder.DropForeignKey(
                name: "FK_order_line_items_fuel_types_fuel_type_id",
                table: "order_line_items");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_batches_fuel_types_fuel_type_id",
                table: "purchase_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_users_parties_party_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_purchase_batches_fuel_type_id",
                table: "purchase_batches");

            migrationBuilder.DropIndex(
                name: "ix_feedback_messages_user_id",
                table: "feedback_messages");

            // The column goes back to NOT NULL, so the rows that now say NULL need the sentinel
            // back before it does - a down migration that cannot run is not a down migration.
            migrationBuilder.Sql(
                """
                UPDATE "provider_event_outbox"
                SET "changed_by_user_id" = '00000000-0000-0000-0000-000000000000'::uuid
                WHERE "changed_by_user_id" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "changed_by_user_id",
                table: "provider_event_outbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
