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
                name: "FK_app_settings_users_updated_by_user_id",
                table: "app_settings",
                column: "updated_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_error_logs_users_resolved_by_user_id",
                table: "error_logs",
                column: "resolved_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

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
                name: "FK_fuel_price_history_users_changed_by_user_id",
                table: "fuel_price_history",
                column: "changed_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_types_users_price_updated_by_user_id",
                table: "fuel_types",
                column: "price_updated_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_operator_voucher_renewals_users_acting_user_id",
                table: "operator_voucher_renewals",
                column: "acting_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_operator_voucher_renewals_users_customer_user_id",
                table: "operator_voucher_renewals",
                column: "customer_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_order_line_items_fuel_types_fuel_type_id",
                table: "order_line_items",
                column: "fuel_type_id",
                principalTable: "fuel_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payments_provider_event_outbox_webhook_event_id",
                table: "payments",
                column: "webhook_event_id",
                principalTable: "provider_event_outbox",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_provider_event_outbox_users_changed_by_user_id",
                table: "provider_event_outbox",
                column: "changed_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_batches_fuel_types_fuel_type_id",
                table: "purchase_batches",
                column: "fuel_type_id",
                principalTable: "fuel_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_settlement_messages_users_resolved_user_id",
                table: "settlement_messages",
                column: "resolved_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

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
                name: "FK_app_settings_users_updated_by_user_id",
                table: "app_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_error_logs_users_resolved_by_user_id",
                table: "error_logs");

            migrationBuilder.DropForeignKey(
                name: "FK_feedback_chat_bindings_users_user_id",
                table: "feedback_chat_bindings");

            migrationBuilder.DropForeignKey(
                name: "FK_feedback_messages_users_user_id",
                table: "feedback_messages");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_price_history_users_changed_by_user_id",
                table: "fuel_price_history");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_types_users_price_updated_by_user_id",
                table: "fuel_types");

            migrationBuilder.DropForeignKey(
                name: "FK_operator_voucher_renewals_users_acting_user_id",
                table: "operator_voucher_renewals");

            migrationBuilder.DropForeignKey(
                name: "FK_operator_voucher_renewals_users_customer_user_id",
                table: "operator_voucher_renewals");

            migrationBuilder.DropForeignKey(
                name: "FK_order_line_items_fuel_types_fuel_type_id",
                table: "order_line_items");

            migrationBuilder.DropForeignKey(
                name: "FK_payments_provider_event_outbox_webhook_event_id",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "FK_provider_event_outbox_users_changed_by_user_id",
                table: "provider_event_outbox");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_batches_fuel_types_fuel_type_id",
                table: "purchase_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_settlement_messages_users_resolved_user_id",
                table: "settlement_messages");

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
