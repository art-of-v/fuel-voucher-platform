using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheIntegrationTablesDocumentThemselves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "webhook_events",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                comment: "Where our processing of the delivery stands.",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "signature",
                table: "webhook_events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "The signature header, so a stored delivery can be re-verified later without the request.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "received_at_utc",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When we received it. Our clock, and the one thing here we are the authority on.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "provider_status",
                table: "webhook_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "The acquirer's status word - 'success', 'failure', 'reversed', 'expired' - in its ownvocabulary, kept exactly as sent rather than mapped onto our status enum. The wholereason a stored event is worth storing is that it says what THEY said.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<DateTime>(
                name: "provider_modified_at_utc",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When the acquirer last changed it.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "provider_event_id",
                table: "webhook_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "The provider's identity for this event, or our stable derivation of it where theprovider supplies none. Unique with the provider: that pair is what makes a REDELIVERYimpossible rather than merely ignored.",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<DateTime>(
                name: "provider_created_at_utc",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When the acquirer says the event happened.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "provider",
                table: "webhook_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "The vendor token, lowercase: 'monobank'.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<DateTime>(
                name: "processed_at_utc",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When processing finished. NULL while a delivery is still outstanding.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "payload",
                table: "webhook_events",
                type: "jsonb",
                nullable: false,
                comment: "The body exactly as received.",
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "key_id",
                table: "webhook_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Which key the signature was made with.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "invoice_id",
                table: "webhook_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "The acquirer's invoice id. Indexed, because every question about an event starts there.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "error",
                table: "webhook_events",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "Why processing failed, when it did.",
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "attempts",
                table: "webhook_events",
                type: "integer",
                nullable: false,
                comment: "Deliveries seen for this event. Above 1 means the acquirer retried us.",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_kopecks",
                table: "webhook_events",
                type: "numeric(14,0)",
                nullable: true,
                comment: "Kopecks, as reported. Evidence of what was charged, independent of our price.",
                oldClrType: typeof(decimal),
                oldType: "numeric(14,0)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "monobank_statement_entries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "The acquirer-side status, in the acquirer's vocabulary.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "statement_id",
                table: "monobank_statement_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "The key this row is filed under. OURS, composed from what the acquirer sends - thestatement carries no id of its own - and stable across re-fetches of the same period,which is what makes re-importing a month idempotent. It is printed in the act anaccountant signs, so it is readable rather than a hash.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "rrn",
                table: "monobank_statement_entries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "The acquirer's retrieval reference number.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "reference",
                table: "monobank_statement_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Our reference, echoed back. Evidence, NOT a key - the same string appears on thousandsof unrelated payments, and a match key built on it would match a customer's own memo tosomebody else's invoice.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "raw_payload",
                table: "monobank_statement_entries",
                type: "text",
                nullable: true,
                comment: "The statement row exactly as received.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "merchant",
                table: "monobank_statement_entries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "Which merchant's statement this came from. The same acquirer id can exist on bothmerchants, so the unique key is scoped by merchant rather than by id alone.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "masked_card",
                table: "monobank_statement_entries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "The card, masked by the acquirer.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "invoice_id",
                table: "monobank_statement_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The acquirer's invoice id, when the row carries one.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "first_seen_at_utc",
                table: "monobank_statement_entries",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When we first saw this row.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "ext_ref",
                table: "monobank_statement_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The acquirer's own reference for the operation.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "destination",
                table: "monobank_statement_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The payment purpose as the acquirer shows it on the receipt. Evidence, and never a key,for the same reason.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ccy",
                table: "monobank_statement_entries",
                type: "integer",
                nullable: true,
                comment: "The currency code, 980 for UAH.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "approval_code",
                table: "monobank_statement_entries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "The acquirer's approval code.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_kopecks",
                table: "monobank_statement_entries",
                type: "numeric(14,0)",
                nullable: true,
                comment: "Kopecks, exactly as Monobank reports them - the acquirer's API speaks kopecks andnothing else, so this is a boundary value and is not converted on the way in.",
                oldClrType: typeof(decimal),
                oldType: "numeric(14,0)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "acquirer_modified_at_utc",
                table: "monobank_statement_entries",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Kept, and empty. The documented statement payload carries one timestamp per row and noseparate modification time, so nothing populates this column today - it stays ratherthan being dropped so a future acquirer field does not need a migration to accept.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "acquirer_created_at_utc",
                table: "monobank_statement_entries",
                type: "timestamp with time zone",
                nullable: true,
                comment: "The financial operation time (the 'date' on the wire). The period the act filters on.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "user_name",
                table: "error_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The user the request was made by, when we know who it was.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "trace_id",
                table: "error_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "The W3C trace id of the activity that was current when the record was logged. One failedrequest emits SEVERAL records - request logging, the global handler, the featurehandler, EF Core - and this is what lets the admin log group them back into a singleincident. NULL for records logged outside any activity: startup, shutdown, backgroundjobs.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "stack_trace",
                table: "error_logs",
                type: "text",
                nullable: true,
                comment: "The stack, when there was one. The longest column in the table, and the one that fillsit.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "source",
                table: "error_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Where the error came from - the logger's source, or the component that raised it.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "resolved_by_user_name",
                table: "error_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Their name at the time.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "resolved_by_user_id",
                table: "error_logs",
                type: "uuid",
                nullable: true,
                comment: "Who acknowledged it.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "resolved_at_utc",
                table: "error_logs",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When staff ACKNOWLEDGED this error as handled, or NULL while it is still outstanding. Afault is only 'resolved' once a human says so - the log cannot tell whether a deploymentfixed the cause.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "request_path",
                table: "error_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "The path of the request that failed, when it was a request.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "request_method",
                table: "error_logs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                comment: "The method of that request.",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "message",
                table: "error_logs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                comment: "The log message, in one line.",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<DateTime>(
                name: "logged_at_utc",
                table: "error_logs",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the error was logged.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "level",
                table: "error_logs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                comment: "How bad it was - information, warning, error, critical.",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "exception_type",
                table: "error_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "The exception's type, when there was one.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "exception_message",
                table: "error_logs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                comment: "The exception's message, when there was one.",
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "webhook_events",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldComment: "Where our processing of the delivery stands.");

            migrationBuilder.AlterColumn<string>(
                name: "signature",
                table: "webhook_events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "The signature header, so a stored delivery can be re-verified later without the request.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "received_at_utc",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When we received it. Our clock, and the one thing here we are the authority on.");

            migrationBuilder.AlterColumn<string>(
                name: "provider_status",
                table: "webhook_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "The acquirer's status word - 'success', 'failure', 'reversed', 'expired' - in its ownvocabulary, kept exactly as sent rather than mapped onto our status enum. The wholereason a stored event is worth storing is that it says what THEY said.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "provider_modified_at_utc",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When the acquirer last changed it.");

            migrationBuilder.AlterColumn<string>(
                name: "provider_event_id",
                table: "webhook_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "The provider's identity for this event, or our stable derivation of it where theprovider supplies none. Unique with the provider: that pair is what makes a REDELIVERYimpossible rather than merely ignored.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "provider_created_at_utc",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When the acquirer says the event happened.");

            migrationBuilder.AlterColumn<string>(
                name: "provider",
                table: "webhook_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "The vendor token, lowercase: 'monobank'.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "processed_at_utc",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When processing finished. NULL while a delivery is still outstanding.");

            migrationBuilder.AlterColumn<string>(
                name: "payload",
                table: "webhook_events",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldComment: "The body exactly as received.");

            migrationBuilder.AlterColumn<string>(
                name: "key_id",
                table: "webhook_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "Which key the signature was made with.");

            migrationBuilder.AlterColumn<string>(
                name: "invoice_id",
                table: "webhook_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "The acquirer's invoice id. Indexed, because every question about an event starts there.");

            migrationBuilder.AlterColumn<string>(
                name: "error",
                table: "webhook_events",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true,
                oldComment: "Why processing failed, when it did.");

            migrationBuilder.AlterColumn<int>(
                name: "attempts",
                table: "webhook_events",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Deliveries seen for this event. Above 1 means the acquirer retried us.");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_kopecks",
                table: "webhook_events",
                type: "numeric(14,0)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,0)",
                oldNullable: true,
                oldComment: "Kopecks, as reported. Evidence of what was charged, independent of our price.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "monobank_statement_entries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldComment: "The acquirer-side status, in the acquirer's vocabulary.");

            migrationBuilder.AlterColumn<string>(
                name: "statement_id",
                table: "monobank_statement_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "The key this row is filed under. OURS, composed from what the acquirer sends - thestatement carries no id of its own - and stable across re-fetches of the same period,which is what makes re-importing a month idempotent. It is printed in the act anaccountant signs, so it is readable rather than a hash.");

            migrationBuilder.AlterColumn<string>(
                name: "rrn",
                table: "monobank_statement_entries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldComment: "The acquirer's retrieval reference number.");

            migrationBuilder.AlterColumn<string>(
                name: "reference",
                table: "monobank_statement_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "Our reference, echoed back. Evidence, NOT a key - the same string appears on thousandsof unrelated payments, and a match key built on it would match a customer's own memo tosomebody else's invoice.");

            migrationBuilder.AlterColumn<string>(
                name: "raw_payload",
                table: "monobank_statement_entries",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "The statement row exactly as received.");

            migrationBuilder.AlterColumn<string>(
                name: "merchant",
                table: "monobank_statement_entries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "Which merchant's statement this came from. The same acquirer id can exist on bothmerchants, so the unique key is scoped by merchant rather than by id alone.");

            migrationBuilder.AlterColumn<string>(
                name: "masked_card",
                table: "monobank_statement_entries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldComment: "The card, masked by the acquirer.");

            migrationBuilder.AlterColumn<string>(
                name: "invoice_id",
                table: "monobank_statement_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The acquirer's invoice id, when the row carries one.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "first_seen_at_utc",
                table: "monobank_statement_entries",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When we first saw this row.");

            migrationBuilder.AlterColumn<string>(
                name: "ext_ref",
                table: "monobank_statement_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The acquirer's own reference for the operation.");

            migrationBuilder.AlterColumn<string>(
                name: "destination",
                table: "monobank_statement_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The payment purpose as the acquirer shows it on the receipt. Evidence, and never a key,for the same reason.");

            migrationBuilder.AlterColumn<int>(
                name: "ccy",
                table: "monobank_statement_entries",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "The currency code, 980 for UAH.");

            migrationBuilder.AlterColumn<string>(
                name: "approval_code",
                table: "monobank_statement_entries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldComment: "The acquirer's approval code.");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_kopecks",
                table: "monobank_statement_entries",
                type: "numeric(14,0)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,0)",
                oldNullable: true,
                oldComment: "Kopecks, exactly as Monobank reports them - the acquirer's API speaks kopecks andnothing else, so this is a boundary value and is not converted on the way in.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "acquirer_modified_at_utc",
                table: "monobank_statement_entries",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "Kept, and empty. The documented statement payload carries one timestamp per row and noseparate modification time, so nothing populates this column today - it stays ratherthan being dropped so a future acquirer field does not need a migration to accept.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "acquirer_created_at_utc",
                table: "monobank_statement_entries",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "The financial operation time (the 'date' on the wire). The period the act filters on.");

            migrationBuilder.AlterColumn<string>(
                name: "user_name",
                table: "error_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The user the request was made by, when we know who it was.");

            migrationBuilder.AlterColumn<string>(
                name: "trace_id",
                table: "error_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "The W3C trace id of the activity that was current when the record was logged. One failedrequest emits SEVERAL records - request logging, the global handler, the featurehandler, EF Core - and this is what lets the admin log group them back into a singleincident. NULL for records logged outside any activity: startup, shutdown, backgroundjobs.");

            migrationBuilder.AlterColumn<string>(
                name: "stack_trace",
                table: "error_logs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "The stack, when there was one. The longest column in the table, and the one that fillsit.");

            migrationBuilder.AlterColumn<string>(
                name: "source",
                table: "error_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "Where the error came from - the logger's source, or the component that raised it.");

            migrationBuilder.AlterColumn<string>(
                name: "resolved_by_user_name",
                table: "error_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "Their name at the time.");

            migrationBuilder.AlterColumn<Guid>(
                name: "resolved_by_user_id",
                table: "error_logs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Who acknowledged it.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "resolved_at_utc",
                table: "error_logs",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When staff ACKNOWLEDGED this error as handled, or NULL while it is still outstanding. Afault is only 'resolved' once a human says so - the log cannot tell whether a deploymentfixed the cause.");

            migrationBuilder.AlterColumn<string>(
                name: "request_path",
                table: "error_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "The path of the request that failed, when it was a request.");

            migrationBuilder.AlterColumn<string>(
                name: "request_method",
                table: "error_logs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true,
                oldComment: "The method of that request.");

            migrationBuilder.AlterColumn<string>(
                name: "message",
                table: "error_logs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldComment: "The log message, in one line.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "logged_at_utc",
                table: "error_logs",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the error was logged.");

            migrationBuilder.AlterColumn<string>(
                name: "level",
                table: "error_logs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldComment: "How bad it was - information, warning, error, critical.");

            migrationBuilder.AlterColumn<string>(
                name: "exception_type",
                table: "error_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "The exception's type, when there was one.");

            migrationBuilder.AlterColumn<string>(
                name: "exception_message",
                table: "error_logs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true,
                oldComment: "The exception's message, when there was one.");
        }
    }
}
