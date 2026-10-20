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
                name: "ix_voucher_movements_actor_party_id",
                table: "voucher_movements",
                column: "actor_party_id");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_movements_actor_user_id",
                table: "voucher_movements",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_events_actor_party_id",
                table: "voucher_events",
                column: "actor_party_id");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_events_actor_user_id",
                table: "voucher_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_messages_resolved_user_id",
                table: "settlement_messages",
                column: "resolved_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_batches_entered_by_user_id",
                table: "purchase_batches",
                column: "entered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_batches_fuel_type_id",
                table: "purchase_batches",
                column: "fuel_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_event_outbox_changed_by_user_id",
                table: "provider_event_outbox",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_events_actor_user_id",
                table: "order_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_operator_voucher_renewals_acting_user_id",
                table: "operator_voucher_renewals",
                column: "acting_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_movement_batches_opened_by_user_id",
                table: "movement_batches",
                column: "opened_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_source_id",
                table: "journal_entries",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_fuel_types_price_updated_by_user_id",
                table: "fuel_types",
                column: "price_updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_messages_user_id",
                table: "feedback_messages",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_error_logs_resolved_by_user_id",
                table: "error_logs",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_app_settings_updated_by_user_id",
                table: "app_settings",
                column: "updated_by_user_id");

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
                name: "ix_voucher_movements_actor_party_id",
                table: "voucher_movements");

            migrationBuilder.DropIndex(
                name: "ix_voucher_movements_actor_user_id",
                table: "voucher_movements");

            migrationBuilder.DropIndex(
                name: "ix_voucher_events_actor_party_id",
                table: "voucher_events");

            migrationBuilder.DropIndex(
                name: "ix_voucher_events_actor_user_id",
                table: "voucher_events");

            migrationBuilder.DropIndex(
                name: "ix_settlement_messages_resolved_user_id",
                table: "settlement_messages");

            migrationBuilder.DropIndex(
                name: "ix_purchase_batches_entered_by_user_id",
                table: "purchase_batches");

            migrationBuilder.DropIndex(
                name: "ix_purchase_batches_fuel_type_id",
                table: "purchase_batches");

            migrationBuilder.DropIndex(
                name: "ix_provider_event_outbox_changed_by_user_id",
                table: "provider_event_outbox");

            migrationBuilder.DropIndex(
                name: "ix_order_events_actor_user_id",
                table: "order_events");

            migrationBuilder.DropIndex(
                name: "ix_operator_voucher_renewals_acting_user_id",
                table: "operator_voucher_renewals");

            migrationBuilder.DropIndex(
                name: "ix_movement_batches_opened_by_user_id",
                table: "movement_batches");

            migrationBuilder.DropIndex(
                name: "ix_journal_entries_source_id",
                table: "journal_entries");

            migrationBuilder.DropIndex(
                name: "ix_fuel_types_price_updated_by_user_id",
                table: "fuel_types");

            migrationBuilder.DropIndex(
                name: "ix_feedback_messages_user_id",
                table: "feedback_messages");

            migrationBuilder.DropIndex(
                name: "ix_error_logs_resolved_by_user_id",
                table: "error_logs");

            migrationBuilder.DropIndex(
                name: "ix_app_settings_updated_by_user_id",
                table: "app_settings");

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
