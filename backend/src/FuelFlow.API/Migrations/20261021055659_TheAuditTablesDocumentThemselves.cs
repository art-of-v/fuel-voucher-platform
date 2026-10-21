using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheAuditTablesDocumentThemselves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "voucher_id",
                table: "voucher_events",
                type: "uuid",
                nullable: false,
                comment: "The voucher this happened to.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "to_status",
                table: "voucher_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                comment: "The status after. Set for a status change only.",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                table: "voucher_events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Why, in the operator's words.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "previous_customer_expiration",
                table: "voucher_events",
                type: "date",
                nullable: true,
                comment: "The expiry before. Set for an extension only - and it is written at the moment it isstill true, because the voucher's own expiry column is overwritten in place.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "payload",
                table: "voucher_events",
                type: "jsonb",
                nullable: true,
                comment: "Anything else worth keeping that has no column of its own.",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "occurred_at_utc",
                table: "voucher_events",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it happened.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "new_customer_expiration",
                table: "voucher_events",
                type: "date",
                nullable: true,
                comment: "The expiry after. Set for an extension only.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "from_status",
                table: "voucher_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                comment: "The status before. Set for a status change only.",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "external_ref",
                table: "voucher_events",
                type: "text",
                nullable: true,
                comment: "What the caller can be pointed back at - an order, a movement, a batch, a command.Indexed, because 'show me everything that happened because of order X' is what turns anevent log into evidence.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "event_type",
                table: "voucher_events",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                comment: "What kind of thing happened - a status change, an expiry being pushed out, a softdelete. See VoucherEventTypes.",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "voucher_events",
                type: "uuid",
                nullable: true,
                comment: "The person who caused it, required when actor_kind is 'user'. A bare uuid, no foreignkey - the record has to survive the account.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_party_id",
                table: "voucher_events",
                type: "uuid",
                nullable: true,
                comment: "The company that caused it. A bare uuid.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "actor_kind",
                table: "voucher_events",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "system",
                comment: "Who caused it: a person, or the scheduler. See VoucherActorKinds.",
                oldClrType: typeof(string),
                oldType: "character varying(12)",
                oldMaxLength: 12,
                oldDefaultValue: "system");

            migrationBuilder.AlterColumn<string>(
                name: "summary",
                table: "provider_event_outbox",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "One line, in the operator's words, saying what changed.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "scope_id",
                table: "provider_event_outbox",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "The provider - the brand - the change happened within, so an audit read can be scoped toone.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "old_value",
                table: "provider_event_outbox",
                type: "jsonb",
                nullable: true,
                comment: "The value before the change, as JSON. NULL when the thing was created: there was novalue before.",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "new_value",
                table: "provider_event_outbox",
                type: "jsonb",
                nullable: false,
                comment: "The value after the change, as JSON.",
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "event_type",
                table: "provider_event_outbox",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "What happened, in the past tense - 'RefundRequested', 'StatusChanged'.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "changed_by_user_name",
                table: "provider_event_outbox",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The name of the person who did it, kept on the row so the record still reads after theaccount is gone.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "changed_by_user_id",
                table: "provider_event_outbox",
                type: "uuid",
                nullable: true,
                comment: "Who did it, when a person did. NULL when a machine did - and NULL rather than theall-zero uuid, so that 'no user' is a value that can still be checked against the userstable.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "changed_at_utc",
                table: "provider_event_outbox",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it happened.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "aggregate_type",
                table: "provider_event_outbox",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "What kind of thing changed - 'Refund', 'Voucher', and the other kinds the audit screengroups by. A short name, not a table name.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "aggregate_id",
                table: "provider_event_outbox",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "Which thing changed, by its own id - a uuid as a string.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "to_status",
                table: "order_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "The status after. Set for a status change only.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "payload",
                table: "order_events",
                type: "jsonb",
                nullable: true,
                comment: "The raw payload that caused the transition, where one exists (a Monobank webhook body).Kept because reconciling against a statement means being able to see what the acquirersaid, not only what we concluded from it.",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                table: "order_events",
                type: "uuid",
                nullable: false,
                comment: "The order this happened to.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "occurred_at_utc",
                table: "order_events",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it happened.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "from_status",
                table: "order_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "The status before. Set for a status change only.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "external_ref",
                table: "order_events",
                type: "text",
                nullable: true,
                comment: "The external reference this transition came from - a Monobank invoice id, usually. Keptdenormalised so the same event can be found without joining the order, which is what aduplicate-delivery investigation looks for.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "event_type",
                table: "order_events",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                comment: "What happened - 'Created', 'PaymentCaptured', 'Fulfilled', 'Refunded', 'StatusChanged'and the rest. A CHECK pins the vocabulary.",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "order_events",
                type: "uuid",
                nullable: true,
                comment: "Who caused it: a user id, a Telegram staff id, or NULL for a system transition. Not aforeign key - the record has to survive the account.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "voucher_id",
                table: "voucher_events",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The voucher this happened to.");

            migrationBuilder.AlterColumn<string>(
                name: "to_status",
                table: "voucher_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true,
                oldComment: "The status after. Set for a status change only.");

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                table: "voucher_events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "Why, in the operator's words.");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "previous_customer_expiration",
                table: "voucher_events",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true,
                oldComment: "The expiry before. Set for an extension only - and it is written at the moment it isstill true, because the voucher's own expiry column is overwritten in place.");

            migrationBuilder.AlterColumn<string>(
                name: "payload",
                table: "voucher_events",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "Anything else worth keeping that has no column of its own.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "occurred_at_utc",
                table: "voucher_events",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it happened.");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "new_customer_expiration",
                table: "voucher_events",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true,
                oldComment: "The expiry after. Set for an extension only.");

            migrationBuilder.AlterColumn<string>(
                name: "from_status",
                table: "voucher_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true,
                oldComment: "The status before. Set for a status change only.");

            migrationBuilder.AlterColumn<string>(
                name: "external_ref",
                table: "voucher_events",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "What the caller can be pointed back at - an order, a movement, a batch, a command.Indexed, because 'show me everything that happened because of order X' is what turns anevent log into evidence.");

            migrationBuilder.AlterColumn<string>(
                name: "event_type",
                table: "voucher_events",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldComment: "What kind of thing happened - a status change, an expiry being pushed out, a softdelete. See VoucherEventTypes.");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "voucher_events",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The person who caused it, required when actor_kind is 'user'. A bare uuid, no foreignkey - the record has to survive the account.");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_party_id",
                table: "voucher_events",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The company that caused it. A bare uuid.");

            migrationBuilder.AlterColumn<string>(
                name: "actor_kind",
                table: "voucher_events",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "system",
                oldClrType: typeof(string),
                oldType: "character varying(12)",
                oldMaxLength: 12,
                oldDefaultValue: "system",
                oldComment: "Who caused it: a person, or the scheduler. See VoucherActorKinds.");

            migrationBuilder.AlterColumn<string>(
                name: "summary",
                table: "provider_event_outbox",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "One line, in the operator's words, saying what changed.");

            migrationBuilder.AlterColumn<string>(
                name: "scope_id",
                table: "provider_event_outbox",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "The provider - the brand - the change happened within, so an audit read can be scoped toone.");

            migrationBuilder.AlterColumn<string>(
                name: "old_value",
                table: "provider_event_outbox",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "The value before the change, as JSON. NULL when the thing was created: there was novalue before.");

            migrationBuilder.AlterColumn<string>(
                name: "new_value",
                table: "provider_event_outbox",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldComment: "The value after the change, as JSON.");

            migrationBuilder.AlterColumn<string>(
                name: "event_type",
                table: "provider_event_outbox",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "What happened, in the past tense - 'RefundRequested', 'StatusChanged'.");

            migrationBuilder.AlterColumn<string>(
                name: "changed_by_user_name",
                table: "provider_event_outbox",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The name of the person who did it, kept on the row so the record still reads after theaccount is gone.");

            migrationBuilder.AlterColumn<Guid>(
                name: "changed_by_user_id",
                table: "provider_event_outbox",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Who did it, when a person did. NULL when a machine did - and NULL rather than theall-zero uuid, so that 'no user' is a value that can still be checked against the userstable.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "changed_at_utc",
                table: "provider_event_outbox",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it happened.");

            migrationBuilder.AlterColumn<string>(
                name: "aggregate_type",
                table: "provider_event_outbox",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "What kind of thing changed - 'Refund', 'Voucher', and the other kinds the audit screengroups by. A short name, not a table name.");

            migrationBuilder.AlterColumn<string>(
                name: "aggregate_id",
                table: "provider_event_outbox",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "Which thing changed, by its own id - a uuid as a string.");

            migrationBuilder.AlterColumn<string>(
                name: "to_status",
                table: "order_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "The status after. Set for a status change only.");

            migrationBuilder.AlterColumn<string>(
                name: "payload",
                table: "order_events",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "The raw payload that caused the transition, where one exists (a Monobank webhook body).Kept because reconciling against a statement means being able to see what the acquirersaid, not only what we concluded from it.");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                table: "order_events",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The order this happened to.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "occurred_at_utc",
                table: "order_events",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it happened.");

            migrationBuilder.AlterColumn<string>(
                name: "from_status",
                table: "order_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "The status before. Set for a status change only.");

            migrationBuilder.AlterColumn<string>(
                name: "external_ref",
                table: "order_events",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "The external reference this transition came from - a Monobank invoice id, usually. Keptdenormalised so the same event can be found without joining the order, which is what aduplicate-delivery investigation looks for.");

            migrationBuilder.AlterColumn<string>(
                name: "event_type",
                table: "order_events",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldComment: "What happened - 'Created', 'PaymentCaptured', 'Fulfilled', 'Refunded', 'StatusChanged'and the rest. A CHECK pins the vocabulary.");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "order_events",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Who caused it: a user id, a Telegram staff id, or NULL for a system transition. Not aforeign key - the record has to survive the account.");
        }
    }
}
