using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheOrderAndTheRestDocumentThemselves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "push_tokens",
                type: "uuid",
                nullable: false,
                comment: "The user the token belongs to.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "push_tokens",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it was last changed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "token",
                table: "push_tokens",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                comment: "The Expo push token. Unique across users - one device, one token, one owner.",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "platform",
                table: "push_tokens",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "The reporting platform of the registering client: ios or android.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_seen_at_utc",
                table: "push_tokens",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the token was last used successfully. A token nobody has heard from in months is atoken to stop sending to.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "push_tokens",
                type: "boolean",
                nullable: false,
                comment: "Cleared when Expo later reports the token is no longer registered. A token that is notactive is not a failure - it is a phone that was replaced.",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                table: "push_tokens",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                comment: "An optional correlation to the registering device. NULL when the client did not say.",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "push_tokens",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the row was written.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "processed_at_utc",
                table: "outbox_events",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When it was published. NULL while it is not.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "processed",
                table: "outbox_events",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether the event has been published. A row that says processed and carries no timecannot say when.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "payload",
                table: "outbox_events",
                type: "jsonb",
                nullable: false,
                comment: "The event itself, as JSON: what the subscriber will receive, frozen at the moment it waswritten.",
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "event_type",
                table: "outbox_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "What kind of event this is - the routing key the publisher reads.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "outbox_events",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the event was written.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "orders",
                type: "uuid",
                nullable: false,
                comment: "The customer who placed the order.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                comment: "When it was last changed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                comment: "Where the order stands: new, partly fulfilled, fulfilled, cancelled. The screens readthis column, and the database holds it to the enum.",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<Guid>(
                name: "source_order_id",
                table: "orders",
                type: "uuid",
                nullable: true,
                comment: "For an issuance, the purchase the fuel originally came from. NULL on purchases andrenewals.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "price",
                table: "orders",
                type: "numeric(12,2)",
                nullable: false,
                comment: "What the customer is charged for the order, in UAH. The order's own number - the linetotals are what the price is made of, and this is what was agreed.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "partially_fulfilled_since_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Since when the order has been partly fulfilled. A row that says 'partly fulfilled' and nodate cannot say how long it has been waiting.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "monobank_status",
                table: "orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "The acquirer's state of the payment, as the acquirer reports it. NULL while nothing isknown.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "monobank_payment_url",
                table: "orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "The payment link the customer is given. It is handed out, not derived: a link that couldbe recomputed would not be a link the acquirer knows.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "monobank_merchant",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "Which merchant the acquirer says the payment went to. The acquirer's word, not ours -which is why it is compared against what we expect rather than being our own field.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "monobank_invoice_id",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "The invoice id the acquirer gave us - the link back to the acquirer's own record. NULLuntil the invoice is created.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "legal_entity_id",
                table: "orders",
                type: "uuid",
                nullable: true,
                comment: "The company the order is for. NULL for an order placed by a person without a company.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_webhook_processed_at_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "The last webhook of the acquirer we have processed - the clock that says we are notbehind.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_webhook_modified_date_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "The modification date of the acquirer's own record, as it was when we last read it. Theacquirer's clock, not ours - which is what makes it worth storing.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Purchase",
                comment: "What this order represents: a purchase, a renewal, or an issuance. Everything createdbefore this column existed is a purchase.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Purchase");

            migrationBuilder.AlterColumn<bool>(
                name: "is_deleted",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Soft delete. The row is hidden, not erased: the orders, the money and the vouchers behindit have to stay readable.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key",
                table: "orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "The key the customer sent with the request. Two requests with the same key are one order,which is what makes a double-tap one order and not two.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "fulfilled_at_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When the order was fulfilled. NULL while it is not.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the row was written.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "voucher_id",
                table: "operator_voucher_renewals",
                type: "uuid",
                nullable: false,
                comment: "The customer's voucher that was renewed (extended) or retired (replace).",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "term_code",
                table: "operator_voucher_renewals",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                comment: "The chosen term as a stable code (1w/2w/1m...6m).",
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8);

            migrationBuilder.AlterColumn<decimal>(
                name: "surcharge_uah",
                table: "operator_voucher_renewals",
                type: "numeric(10,2)",
                nullable: false,
                comment: "The доплата the customer paid off-platform; 0 means a free compensation. Audit only - itnever reprices the stock.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)");

            migrationBuilder.AlterColumn<Guid>(
                name: "replacement_voucher_id",
                table: "operator_voucher_renewals",
                type: "uuid",
                nullable: true,
                comment: "The fresh stock voucher handed to the customer on the replace branch, or NULL on theextend branch (the same voucher was kept).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "old_expiration",
                table: "operator_voucher_renewals",
                type: "date",
                nullable: false,
                comment: "The expiry of the customer's voucher before the renewal.",
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "new_expiration",
                table: "operator_voucher_renewals",
                type: "date",
                nullable: false,
                comment: "The expiry the customer ends up with - the extended voucher's new expiry, or the stockvoucher's expiry.",
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<string>(
                name: "invoice_number",
                table: "operator_voucher_renewals",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "The brand's накладна / invoice number, when there is one.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "invoice_date",
                table: "operator_voucher_renewals",
                type: "date",
                nullable: true,
                comment: "The brand's накладна / invoice date, when there is one.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "customer_user_id",
                table: "operator_voucher_renewals",
                type: "uuid",
                nullable: false,
                comment: "The customer who owns the voucher at the time of the renewal.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "operator_voucher_renewals",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the renewal was recorded.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "branch",
                table: "operator_voucher_renewals",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                comment: "Which branch ran: extend or replace.",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "acting_user_name",
                table: "operator_voucher_renewals",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Their display name at the time, for the audit.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "acting_user_id",
                table: "operator_voucher_renewals",
                type: "uuid",
                nullable: false,
                comment: "The admin who recorded the renewal.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<decimal>(
                name: "surcharge_uah",
                table: "movement_batches",
                type: "numeric(14,2)",
                nullable: false,
                comment: "What the brand charged for the settlement AS A WHOLE. This is the amount on the command,not the sum of the rows' shares: each exchange row carries only its own share, and thoseshares sum back to the total only when every retired voucher was paired with areplacement. An exchange that returns vouchers without reissuing them contributes retiredlitres to the numerators and nothing to the denominator, so the shares over-count. Thenumber the brand charged is the authoritative one, so it is this.",
                oldClrType: typeof(decimal),
                oldType: "numeric(14,2)");

            migrationBuilder.AlterColumn<Guid>(
                name: "supplier_id",
                table: "movement_batches",
                type: "uuid",
                nullable: true,
                comment: "The counterparty. NULL for a batch that is not a supplier settlement.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "movement_batches",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                comment: "The lifecycle state. A batch is created already confirmed, because that is the only waythe application makes one: the operator confirms the exchange and the batch records it inthe same transaction. The rest of the set is the forward shape - a draft that is opened andlater settled is the obvious next workflow - and the CHECK is what stops an unrecognisedword arriving as a status later.",
                oldClrType: typeof(string),
                oldType: "character varying(12)",
                oldMaxLength: 12);

            migrationBuilder.AlterColumn<string>(
                name: "rejection_reason",
                table: "movement_batches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Why a batch was rejected, when it was.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "opened_by_user_name",
                table: "movement_batches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "That operator's name as it read at the time. A copy, so the record stays readable after arename - never a join target.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "opened_by_user_id",
                table: "movement_batches",
                type: "uuid",
                nullable: false,
                comment: "The operator who opened the settlement.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "opened_at_utc",
                table: "movement_batches",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the settlement was opened.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "movement_batches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "What kind of settlement this is - an exchange, a return, a write-off.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "movement_batches",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the row was written.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "confirmed_at_utc",
                table: "movement_batches",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When the settlement was confirmed. Non-null exactly when the status is confirmed, whichis a database CHECK rather than a convention: a batch that says confirmed and carries notimestamp cannot answer 'when'.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "voucher_id",
                table: "fulfillments",
                type: "uuid",
                nullable: false,
                comment: "The voucher that was handed over.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "released_at_utc",
                table: "fulfillments",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Set when the reconciliation takes the voucher back off this order, NULL while the deliverystands. The row is kept either way: the fulfilment records that a delivery happened, andthe correction is the fact that it was undone - not a reason to erase the first part.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "release_reason",
                table: "fulfillments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "Why the voucher was returned to stock. NULL while the delivery stands.",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                table: "fulfillments",
                type: "uuid",
                nullable: false,
                comment: "The order that delivered this voucher.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "line_item_id",
                table: "fulfillments",
                type: "uuid",
                nullable: true,
                comment: "Which line of the order this voucher satisfied. NULL is the honest state of the data: afulfilment without it says only that the order delivered a voucher, and thereconciliation cannot tell two lines of the same volume apart - which is how an order for'OKKO A95 10 L x2' and 'WOG A95 10 L x1' can pass while the customer is short a line.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "fulfilled_at_utc",
                table: "fulfillments",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the voucher was handed over.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "feedback_message_replies",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                comment: "What was written.",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "staff_user_name",
                table: "feedback_message_replies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Their display name as it was at the moment of sending - Telegram ids are not a readableaudit trail.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "staff_user_id",
                table: "feedback_message_replies",
                type: "bigint",
                nullable: false,
                comment: "The Telegram id of the staff member who wrote it.",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<DateTime>(
                name: "sent_at_utc",
                table: "feedback_message_replies",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it was sent.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "feedback_message_id",
                table: "feedback_message_replies",
                type: "uuid",
                nullable: false,
                comment: "The message this replies to.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "delivery_error",
                table: "feedback_message_replies",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "The last delivery failure reason, truncated. NULL on success.",
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "delivered_message_id",
                table: "feedback_message_replies",
                type: "bigint",
                nullable: true,
                comment: "The id the reply got in the customer's chat, or NULL when the delivery failed.",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "feedback_chat_bindings",
                type: "uuid",
                nullable: false,
                comment: "The user whose chat it is.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "bound_at_utc",
                table: "feedback_chat_bindings",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the chat was bound.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<long>(
                name: "telegram_chat_id",
                table: "feedback_chat_bindings",
                type: "bigint",
                nullable: false,
                comment: "The Telegram chat id of the customer's private chat.",
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<string>(
                name: "value",
                table: "app_settings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                comment: "The value, as text. Every setting is text here, and the one that is a number says so inits own name.",
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AlterColumn<string>(
                name: "updated_by_user_name",
                table: "app_settings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Their name at the time. A copy, so the record stays readable after a rename.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "updated_by_user_id",
                table: "app_settings",
                type: "uuid",
                nullable: true,
                comment: "Who changed it. NULL when the change came from a job rather than from a person.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "app_settings",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the setting was last changed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "key",
                table: "app_settings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "The setting's name. It IS the key - the row's identity is its name, not a number.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "push_tokens",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The user the token belongs to.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "push_tokens",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it was last changed.");

            migrationBuilder.AlterColumn<string>(
                name: "token",
                table: "push_tokens",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldComment: "The Expo push token. Unique across users - one device, one token, one owner.");

            migrationBuilder.AlterColumn<string>(
                name: "platform",
                table: "push_tokens",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "The reporting platform of the registering client: ios or android.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_seen_at_utc",
                table: "push_tokens",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the token was last used successfully. A token nobody has heard from in months is atoken to stop sending to.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "push_tokens",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Cleared when Expo later reports the token is no longer registered. A token that is notactive is not a failure - it is a phone that was replaced.");

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                table: "push_tokens",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true,
                oldComment: "An optional correlation to the registering device. NULL when the client did not say.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "push_tokens",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the row was written.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "processed_at_utc",
                table: "outbox_events",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When it was published. NULL while it is not.");

            migrationBuilder.AlterColumn<bool>(
                name: "processed",
                table: "outbox_events",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false,
                oldComment: "Whether the event has been published. A row that says processed and carries no timecannot say when.");

            migrationBuilder.AlterColumn<string>(
                name: "payload",
                table: "outbox_events",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldComment: "The event itself, as JSON: what the subscriber will receive, frozen at the moment it waswritten.");

            migrationBuilder.AlterColumn<string>(
                name: "event_type",
                table: "outbox_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "What kind of event this is - the routing key the publisher reads.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "outbox_events",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the event was written.");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "orders",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The customer who placed the order.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()",
                oldComment: "When it was last changed.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldComment: "Where the order stands: new, partly fulfilled, fulfilled, cancelled. The screens readthis column, and the database holds it to the enum.");

            migrationBuilder.AlterColumn<Guid>(
                name: "source_order_id",
                table: "orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "For an issuance, the purchase the fuel originally came from. NULL on purchases andrenewals.");

            migrationBuilder.AlterColumn<decimal>(
                name: "price",
                table: "orders",
                type: "numeric(12,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldComment: "What the customer is charged for the order, in UAH. The order's own number - the linetotals are what the price is made of, and this is what was agreed.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "partially_fulfilled_since_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "Since when the order has been partly fulfilled. A row that says 'partly fulfilled' and nodate cannot say how long it has been waiting.");

            migrationBuilder.AlterColumn<string>(
                name: "monobank_status",
                table: "orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldComment: "The acquirer's state of the payment, as the acquirer reports it. NULL while nothing isknown.");

            migrationBuilder.AlterColumn<string>(
                name: "monobank_payment_url",
                table: "orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "The payment link the customer is given. It is handed out, not derived: a link that couldbe recomputed would not be a link the acquirer knows.");

            migrationBuilder.AlterColumn<string>(
                name: "monobank_merchant",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "Which merchant the acquirer says the payment went to. The acquirer's word, not ours -which is why it is compared against what we expect rather than being our own field.");

            migrationBuilder.AlterColumn<string>(
                name: "monobank_invoice_id",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "The invoice id the acquirer gave us - the link back to the acquirer's own record. NULLuntil the invoice is created.");

            migrationBuilder.AlterColumn<Guid>(
                name: "legal_entity_id",
                table: "orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The company the order is for. NULL for an order placed by a person without a company.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_webhook_processed_at_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "The last webhook of the acquirer we have processed - the clock that says we are notbehind.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_webhook_modified_date_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "The modification date of the acquirer's own record, as it was when we last read it. Theacquirer's clock, not ours - which is what makes it worth storing.");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Purchase",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Purchase",
                oldComment: "What this order represents: a purchase, a renewal, or an issuance. Everything createdbefore this column existed is a purchase.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_deleted",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false,
                oldComment: "Soft delete. The row is hidden, not erased: the orders, the money and the vouchers behindit have to stay readable.");

            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key",
                table: "orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true,
                oldComment: "The key the customer sent with the request. Two requests with the same key are one order,which is what makes a double-tap one order and not two.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "fulfilled_at_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When the order was fulfilled. NULL while it is not.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the row was written.");

            migrationBuilder.AlterColumn<Guid>(
                name: "voucher_id",
                table: "operator_voucher_renewals",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The customer's voucher that was renewed (extended) or retired (replace).");

            migrationBuilder.AlterColumn<string>(
                name: "term_code",
                table: "operator_voucher_renewals",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldComment: "The chosen term as a stable code (1w/2w/1m...6m).");

            migrationBuilder.AlterColumn<decimal>(
                name: "surcharge_uah",
                table: "operator_voucher_renewals",
                type: "numeric(10,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldComment: "The доплата the customer paid off-platform; 0 means a free compensation. Audit only - itnever reprices the stock.");

            migrationBuilder.AlterColumn<Guid>(
                name: "replacement_voucher_id",
                table: "operator_voucher_renewals",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The fresh stock voucher handed to the customer on the replace branch, or NULL on theextend branch (the same voucher was kept).");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "old_expiration",
                table: "operator_voucher_renewals",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldComment: "The expiry of the customer's voucher before the renewal.");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "new_expiration",
                table: "operator_voucher_renewals",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldComment: "The expiry the customer ends up with - the extended voucher's new expiry, or the stockvoucher's expiry.");

            migrationBuilder.AlterColumn<string>(
                name: "invoice_number",
                table: "operator_voucher_renewals",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "The brand's накладна / invoice number, when there is one.");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "invoice_date",
                table: "operator_voucher_renewals",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true,
                oldComment: "The brand's накладна / invoice date, when there is one.");

            migrationBuilder.AlterColumn<Guid>(
                name: "customer_user_id",
                table: "operator_voucher_renewals",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The customer who owns the voucher at the time of the renewal.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "operator_voucher_renewals",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the renewal was recorded.");

            migrationBuilder.AlterColumn<string>(
                name: "branch",
                table: "operator_voucher_renewals",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldComment: "Which branch ran: extend or replace.");

            migrationBuilder.AlterColumn<string>(
                name: "acting_user_name",
                table: "operator_voucher_renewals",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "Their display name at the time, for the audit.");

            migrationBuilder.AlterColumn<Guid>(
                name: "acting_user_id",
                table: "operator_voucher_renewals",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The admin who recorded the renewal.");

            migrationBuilder.AlterColumn<decimal>(
                name: "surcharge_uah",
                table: "movement_batches",
                type: "numeric(14,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,2)",
                oldComment: "What the brand charged for the settlement AS A WHOLE. This is the amount on the command,not the sum of the rows' shares: each exchange row carries only its own share, and thoseshares sum back to the total only when every retired voucher was paired with areplacement. An exchange that returns vouchers without reissuing them contributes retiredlitres to the numerators and nothing to the denominator, so the shares over-count. Thenumber the brand charged is the authoritative one, so it is this.");

            migrationBuilder.AlterColumn<Guid>(
                name: "supplier_id",
                table: "movement_batches",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The counterparty. NULL for a batch that is not a supplier settlement.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "movement_batches",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(12)",
                oldMaxLength: 12,
                oldComment: "The lifecycle state. A batch is created already confirmed, because that is the only waythe application makes one: the operator confirms the exchange and the batch records it inthe same transaction. The rest of the set is the forward shape - a draft that is opened andlater settled is the obvious next workflow - and the CHECK is what stops an unrecognisedword arriving as a status later.");

            migrationBuilder.AlterColumn<string>(
                name: "rejection_reason",
                table: "movement_batches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "Why a batch was rejected, when it was.");

            migrationBuilder.AlterColumn<string>(
                name: "opened_by_user_name",
                table: "movement_batches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "That operator's name as it read at the time. A copy, so the record stays readable after arename - never a join target.");

            migrationBuilder.AlterColumn<Guid>(
                name: "opened_by_user_id",
                table: "movement_batches",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The operator who opened the settlement.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "opened_at_utc",
                table: "movement_batches",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the settlement was opened.");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "movement_batches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "What kind of settlement this is - an exchange, a return, a write-off.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "movement_batches",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the row was written.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "confirmed_at_utc",
                table: "movement_batches",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When the settlement was confirmed. Non-null exactly when the status is confirmed, whichis a database CHECK rather than a convention: a batch that says confirmed and carries notimestamp cannot answer 'when'.");

            migrationBuilder.AlterColumn<Guid>(
                name: "voucher_id",
                table: "fulfillments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The voucher that was handed over.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "released_at_utc",
                table: "fulfillments",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "Set when the reconciliation takes the voucher back off this order, NULL while the deliverystands. The row is kept either way: the fulfilment records that a delivery happened, andthe correction is the fact that it was undone - not a reason to erase the first part.");

            migrationBuilder.AlterColumn<string>(
                name: "release_reason",
                table: "fulfillments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true,
                oldComment: "Why the voucher was returned to stock. NULL while the delivery stands.");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                table: "fulfillments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The order that delivered this voucher.");

            migrationBuilder.AlterColumn<Guid>(
                name: "line_item_id",
                table: "fulfillments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Which line of the order this voucher satisfied. NULL is the honest state of the data: afulfilment without it says only that the order delivered a voucher, and thereconciliation cannot tell two lines of the same volume apart - which is how an order for'OKKO A95 10 L x2' and 'WOG A95 10 L x1' can pass while the customer is short a line.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "fulfilled_at_utc",
                table: "fulfillments",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the voucher was handed over.");

            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "feedback_message_replies",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldComment: "What was written.");

            migrationBuilder.AlterColumn<string>(
                name: "staff_user_name",
                table: "feedback_message_replies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "Their display name as it was at the moment of sending - Telegram ids are not a readableaudit trail.");

            migrationBuilder.AlterColumn<long>(
                name: "staff_user_id",
                table: "feedback_message_replies",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "The Telegram id of the staff member who wrote it.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "sent_at_utc",
                table: "feedback_message_replies",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it was sent.");

            migrationBuilder.AlterColumn<Guid>(
                name: "feedback_message_id",
                table: "feedback_message_replies",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The message this replies to.");

            migrationBuilder.AlterColumn<string>(
                name: "delivery_error",
                table: "feedback_message_replies",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true,
                oldComment: "The last delivery failure reason, truncated. NULL on success.");

            migrationBuilder.AlterColumn<long>(
                name: "delivered_message_id",
                table: "feedback_message_replies",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true,
                oldComment: "The id the reply got in the customer's chat, or NULL when the delivery failed.");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "feedback_chat_bindings",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The user whose chat it is.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "bound_at_utc",
                table: "feedback_chat_bindings",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the chat was bound.");

            migrationBuilder.AlterColumn<long>(
                name: "telegram_chat_id",
                table: "feedback_chat_bindings",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "The Telegram chat id of the customer's private chat.")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<string>(
                name: "value",
                table: "app_settings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldComment: "The value, as text. Every setting is text here, and the one that is a number says so inits own name.");

            migrationBuilder.AlterColumn<string>(
                name: "updated_by_user_name",
                table: "app_settings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "Their name at the time. A copy, so the record stays readable after a rename.");

            migrationBuilder.AlterColumn<Guid>(
                name: "updated_by_user_id",
                table: "app_settings",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Who changed it. NULL when the change came from a job rather than from a person.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "app_settings",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the setting was last changed.");

            migrationBuilder.AlterColumn<string>(
                name: "key",
                table: "app_settings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "The setting's name. It IS the key - the row's identity is its name, not a number.");
        }
    }
}
