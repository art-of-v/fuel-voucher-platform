using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheMoneyTablesDocumentThemselves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "valid_to",
                table: "voucher_term_pricing",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When it stopped. NULL means this is the current row - and only one may be open.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "valid_from",
                table: "voucher_term_pricing",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When this row started applying.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "updated_by_user_name",
                table: "voucher_term_pricing",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The name of that user, as it read at the time.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "updated_by_user_id",
                table: "voucher_term_pricing",
                type: "uuid",
                nullable: true,
                comment: "Who closed the previous row and opened this one. NULL on the rows the migration creates,because nobody opened them - that is the honest value.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "term_code",
                table: "voucher_term_pricing",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                comment: "The purchase term this row prices, from VoucherRenewalTerms.Code - '1w', '2m' and so on.A code rather than a duration, so the ladder stays aligned with the term the customeractually chose.",
                oldClrType: typeof(string),
                oldType: "character varying(4)",
                oldMaxLength: 4);

            migrationBuilder.AlterColumn<string>(
                name: "scheme",
                table: "voucher_term_pricing",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                comment: "Which ladder this row belongs to - one of VoucherTermPricingSchemes.",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<decimal>(
                name: "rate_per_liter",
                table: "voucher_term_pricing",
                type: "numeric(14,4)",
                nullable: false,
                comment: "The number, in UAH per litre - and what it means depends on the scheme: an absoluteprice for a renewal, a subtraction for a purchase term.",
                oldClrType: typeof(decimal),
                oldType: "numeric(14,4)");

            migrationBuilder.AlterColumn<bool>(
                name: "enabled",
                table: "voucher_term_pricing",
                type: "boolean",
                nullable: false,
                comment: "Whether the operator put this tier on sale. Never acts alone: a tier is offerable onlywhen it is enabled AND its rate is positive, so a row left on at zero sells nothing.",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "settlement_messages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "The message as written, trimmed. NULL only for a message that carried no text at all.",
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "telegram_user_id",
                table: "settlement_messages",
                type: "bigint",
                nullable: false,
                comment: "Who sent it. Kept even for a refused message: the refusal is only meaningful with thesender, and an authorisation row that cannot name who was refused names nothing.",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "telegram_display_name",
                table: "settlement_messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The display name at the time, so the record survives the person renaming themselves.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "sent_at_utc",
                table: "settlement_messages",
                type: "timestamp with time zone",
                nullable: false,
                comment: "Telegram's own timestamp for the message, not our receive time.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "resolved_user_id",
                table: "settlement_messages",
                type: "uuid",
                nullable: true,
                comment: "The customer the code resolved to, when it resolved to one.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "rejection_reason",
                table: "settlement_messages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Why, when the outcome is not Accepted.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "payment_id",
                table: "settlement_messages",
                type: "uuid",
                nullable: true,
                comment: "The payment this produced, when it produced one.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "outcome",
                table: "settlement_messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "What the message turned out to be: accepted, or a refusal.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<long>(
                name: "message_id",
                table: "settlement_messages",
                type: "bigint",
                nullable: false,
                comment: "Telegram's per-chat message id. Part of the idempotency key.",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "settlement_messages",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true,
                comment: "The code as read, upper-cased. Kept for the same reason as the amount.",
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "chat_id",
                table: "settlement_messages",
                type: "bigint",
                nullable: false,
                comment: "Telegram's chat id. Part of the idempotency key, because message ids are per-chat.",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_uah",
                table: "settlement_messages",
                type: "numeric(12,2)",
                nullable: true,
                comment: "What the parser read, kept even when nothing matched.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "webhook_event_id",
                table: "payments",
                type: "uuid",
                nullable: true,
                comment: "The delivery that proved this payment, when the proof was a webhook. NULL when thepayment was settled from the statement instead - which is itself the answer to 'did theacquirer tell us, or did we find out on our own?'",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "payments",
                type: "uuid",
                nullable: true,
                comment: "The customer this money belongs to, when we know who paid. NULL together with a NULLorder_id means the payment is UNALLOCATED: the money is real and belongs to nobody yet,and an operator decides whose it is.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "payments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                comment: "Where this payment stands.",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "provider_payment_id",
                table: "payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "The acquirer's own payment-system id, where it provides one. Distinct from the invoiceid: several payments can be attempted against one invoice, and the statement rows carrythis value as rrn/approvalCode.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "provider_payload",
                table: "payments",
                type: "text",
                nullable: true,
                comment: "The acquirer's row verbatim, when we have one.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "provider_invoice_id",
                table: "payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "The acquirer's invoice id - the identity that survives a re-issued payment link, andunique per provider and per kind, so two attempts at paying the same order coexistinstead of overwriting one another.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "provider_amount_kopecks",
                table: "payments",
                type: "numeric(14,0)",
                nullable: true,
                comment: "What the ACQUIRER reported charging, once a callback or a statement row says so. NULLuntil one arrives, and deliberately NOT constrained against amount_kopecks: the twodisagreeing is the entire point of the column, because it makes an amount mismatch aqueryable fact on the money row instead of a log line that is gone by the morning.",
                oldClrType: typeof(decimal),
                oldType: "numeric(14,0)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "provider",
                table: "payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "The acquiring brand, lowercase, matching the Monobank vendor token.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<Guid>(
                name: "parent_payment_id",
                table: "payments",
                type: "uuid",
                nullable: true,
                comment: "The capture this refund reverses. NULL on a capture - a refund is always answerable as'money back against this exact charge'.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "paid_at_utc",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When the money moved - written from the acquirer's statement when one arrives, becausethe statement is the authority on time as well as on amount.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                table: "payments",
                type: "uuid",
                nullable: true,
                comment: "The order this payment settles, or NULL when it is not yet attached to one. NULL is notan error and not a pending payment: it is money we hold that is not yet against a sale -an advance, which is a liability, not revenue. Revenue is recognised when the fuel isdelivered, never on receipt.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "payments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                comment: "What this row is: a capture, or a refund.",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<bool>(
                name: "is_automatic",
                table: "payments",
                type: "boolean",
                nullable: false,
                comment: "The refund was raised by a system rule rather than by a person.",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "error_message",
                table: "payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Why the refund could not be completed, when it could not.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "currency",
                table: "payments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                comment: "The currency the amounts are in.",
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

            migrationBuilder.AlterColumn<string>(
                name: "created_by_user_name",
                table: "payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The approver's name as it read at approval time. A denormalised copy, never a jointarget.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_user_id",
                table: "payments",
                type: "uuid",
                nullable: true,
                comment: "Who approved the refund, and whether it was automatic. Audit of the decision, not of themoney - only a refund has one, and the database refuses these on a capture.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_uah",
                table: "payments",
                type: "numeric(12,2)",
                nullable: false,
                comment: "What WE billed, in UAH. The same type as orders.price.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_kopecks",
                table: "payments",
                type: "numeric(14,0)",
                nullable: false,
                comment: "What the wire said, in kopecks. A second representation of the same number - and a CHECKholds the two in agreement.",
                oldClrType: typeof(decimal),
                oldType: "numeric(14,0)");

            migrationBuilder.AlterColumn<string>(
                name: "tamper_hash",
                table: "journal_entries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "SHA-256 over the entry's own content. A hash that anyone can recompute is not asignature.",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "journal_entries",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Draft",
                comment: "Draft or posted. A draft is not in the books, and the balance triggers count only postedentries.",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "Draft");

            migrationBuilder.AlterColumn<string>(
                name: "source_type",
                table: "journal_entries",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                comment: "What caused the entry: a payment, a refund, a fulfilment.",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);

            migrationBuilder.AlterColumn<Guid>(
                name: "source_id",
                table: "journal_entries",
                type: "uuid",
                nullable: true,
                comment: "The operational row that caused this entry, where there is exactly one.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "source_event",
                table: "journal_entries",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                comment: "The domain event that caused this entry, matching ledger_mapping_rules.source_event.Stored on the entry and not just inferred from the mapping, because one business factcan produce several entries - a sale posts revenue, then VAT, then cost of sales - andidempotency has to tell those apart.",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<Guid>(
                name: "reversed_by_entry_id",
                table: "journal_entries",
                type: "uuid",
                nullable: true,
                comment: "The entry that reverses this one. A posted entry is never edited or deleted - it isreversed.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "posted_by_user_id",
                table: "journal_entries",
                type: "uuid",
                nullable: true,
                comment: "Who posted it.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "posted_at_utc",
                table: "journal_entries",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When the entry was posted. NULL while it is a draft.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "period_id",
                table: "journal_entries",
                type: "uuid",
                nullable: false,
                comment: "The accounting period this entry falls in.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<long>(
                name: "entry_no",
                table: "journal_entries",
                type: "bigint",
                nullable: false,
                comment: "The gapless number of this entry within its period. Allocated from document_sequences,not from here.",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "entry_date",
                table: "journal_entries",
                type: "date",
                nullable: false,
                comment: "The date the entry belongs to - the accounting period it lands in, not the moment it waswritten.",
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "journal_entries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "What the entry is for, in the accountant's words.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "valid_to",
                table: "voucher_term_pricing",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When it stopped. NULL means this is the current row - and only one may be open.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "valid_from",
                table: "voucher_term_pricing",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When this row started applying.");

            migrationBuilder.AlterColumn<string>(
                name: "updated_by_user_name",
                table: "voucher_term_pricing",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The name of that user, as it read at the time.");

            migrationBuilder.AlterColumn<Guid>(
                name: "updated_by_user_id",
                table: "voucher_term_pricing",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Who closed the previous row and opened this one. NULL on the rows the migration creates,because nobody opened them - that is the honest value.");

            migrationBuilder.AlterColumn<string>(
                name: "term_code",
                table: "voucher_term_pricing",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4)",
                oldMaxLength: 4,
                oldComment: "The purchase term this row prices, from VoucherRenewalTerms.Code - '1w', '2m' and so on.A code rather than a duration, so the ladder stays aligned with the term the customeractually chose.");

            migrationBuilder.AlterColumn<string>(
                name: "scheme",
                table: "voucher_term_pricing",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldComment: "Which ladder this row belongs to - one of VoucherTermPricingSchemes.");

            migrationBuilder.AlterColumn<decimal>(
                name: "rate_per_liter",
                table: "voucher_term_pricing",
                type: "numeric(14,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,4)",
                oldComment: "The number, in UAH per litre - and what it means depends on the scheme: an absoluteprice for a renewal, a subtraction for a purchase term.");

            migrationBuilder.AlterColumn<bool>(
                name: "enabled",
                table: "voucher_term_pricing",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Whether the operator put this tier on sale. Never acts alone: a tier is offerable onlywhen it is enabled AND its rate is positive, so a row left on at zero sells nothing.");

            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "settlement_messages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true,
                oldComment: "The message as written, trimmed. NULL only for a message that carried no text at all.");

            migrationBuilder.AlterColumn<long>(
                name: "telegram_user_id",
                table: "settlement_messages",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "Who sent it. Kept even for a refused message: the refusal is only meaningful with thesender, and an authorisation row that cannot name who was refused names nothing.");

            migrationBuilder.AlterColumn<string>(
                name: "telegram_display_name",
                table: "settlement_messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The display name at the time, so the record survives the person renaming themselves.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "sent_at_utc",
                table: "settlement_messages",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "Telegram's own timestamp for the message, not our receive time.");

            migrationBuilder.AlterColumn<Guid>(
                name: "resolved_user_id",
                table: "settlement_messages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The customer the code resolved to, when it resolved to one.");

            migrationBuilder.AlterColumn<string>(
                name: "rejection_reason",
                table: "settlement_messages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "Why, when the outcome is not Accepted.");

            migrationBuilder.AlterColumn<Guid>(
                name: "payment_id",
                table: "settlement_messages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The payment this produced, when it produced one.");

            migrationBuilder.AlterColumn<string>(
                name: "outcome",
                table: "settlement_messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "What the message turned out to be: accepted, or a refusal.");

            migrationBuilder.AlterColumn<long>(
                name: "message_id",
                table: "settlement_messages",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "Telegram's per-chat message id. Part of the idempotency key.");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "settlement_messages",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldNullable: true,
                oldComment: "The code as read, upper-cased. Kept for the same reason as the amount.");

            migrationBuilder.AlterColumn<long>(
                name: "chat_id",
                table: "settlement_messages",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "Telegram's chat id. Part of the idempotency key, because message ids are per-chat.");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_uah",
                table: "settlement_messages",
                type: "numeric(12,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true,
                oldComment: "What the parser read, kept even when nothing matched.");

            migrationBuilder.AlterColumn<Guid>(
                name: "webhook_event_id",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The delivery that proved this payment, when the proof was a webhook. NULL when thepayment was settled from the statement instead - which is itself the answer to 'did theacquirer tell us, or did we find out on our own?'");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The customer this money belongs to, when we know who paid. NULL together with a NULLorder_id means the payment is UNALLOCATED: the money is real and belongs to nobody yet,and an operator decides whose it is.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "payments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldComment: "Where this payment stands.");

            migrationBuilder.AlterColumn<string>(
                name: "provider_payment_id",
                table: "payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "The acquirer's own payment-system id, where it provides one. Distinct from the invoiceid: several payments can be attempted against one invoice, and the statement rows carrythis value as rrn/approvalCode.");

            migrationBuilder.AlterColumn<string>(
                name: "provider_payload",
                table: "payments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "The acquirer's row verbatim, when we have one.");

            migrationBuilder.AlterColumn<string>(
                name: "provider_invoice_id",
                table: "payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "The acquirer's invoice id - the identity that survives a re-issued payment link, andunique per provider and per kind, so two attempts at paying the same order coexistinstead of overwriting one another.");

            migrationBuilder.AlterColumn<decimal>(
                name: "provider_amount_kopecks",
                table: "payments",
                type: "numeric(14,0)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,0)",
                oldNullable: true,
                oldComment: "What the ACQUIRER reported charging, once a callback or a statement row says so. NULLuntil one arrives, and deliberately NOT constrained against amount_kopecks: the twodisagreeing is the entire point of the column, because it makes an amount mismatch aqueryable fact on the money row instead of a log line that is gone by the morning.");

            migrationBuilder.AlterColumn<string>(
                name: "provider",
                table: "payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "The acquiring brand, lowercase, matching the Monobank vendor token.");

            migrationBuilder.AlterColumn<Guid>(
                name: "parent_payment_id",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The capture this refund reverses. NULL on a capture - a refund is always answerable as'money back against this exact charge'.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "paid_at_utc",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When the money moved - written from the acquirer's statement when one arrives, becausethe statement is the authority on time as well as on amount.");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The order this payment settles, or NULL when it is not yet attached to one. NULL is notan error and not a pending payment: it is money we hold that is not yet against a sale -an advance, which is a liability, not revenue. Revenue is recognised when the fuel isdelivered, never on receipt.");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "payments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldComment: "What this row is: a capture, or a refund.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_automatic",
                table: "payments",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "The refund was raised by a system rule rather than by a person.");

            migrationBuilder.AlterColumn<string>(
                name: "error_message",
                table: "payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "Why the refund could not be completed, when it could not.");

            migrationBuilder.AlterColumn<string>(
                name: "currency",
                table: "payments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldComment: "The currency the amounts are in.");

            migrationBuilder.AlterColumn<string>(
                name: "created_by_user_name",
                table: "payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The approver's name as it read at approval time. A denormalised copy, never a jointarget.");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_user_id",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Who approved the refund, and whether it was automatic. Audit of the decision, not of themoney - only a refund has one, and the database refuses these on a capture.");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_uah",
                table: "payments",
                type: "numeric(12,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldComment: "What WE billed, in UAH. The same type as orders.price.");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_kopecks",
                table: "payments",
                type: "numeric(14,0)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,0)",
                oldComment: "What the wire said, in kopecks. A second representation of the same number - and a CHECKholds the two in agreement.");

            migrationBuilder.AlterColumn<string>(
                name: "tamper_hash",
                table: "journal_entries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true,
                oldComment: "SHA-256 over the entry's own content. A hash that anyone can recompute is not asignature.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "journal_entries",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Draft",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "Draft",
                oldComment: "Draft or posted. A draft is not in the books, and the balance triggers count only postedentries.");

            migrationBuilder.AlterColumn<string>(
                name: "source_type",
                table: "journal_entries",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldComment: "What caused the entry: a payment, a refund, a fulfilment.");

            migrationBuilder.AlterColumn<Guid>(
                name: "source_id",
                table: "journal_entries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The operational row that caused this entry, where there is exactly one.");

            migrationBuilder.AlterColumn<string>(
                name: "source_event",
                table: "journal_entries",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldComment: "The domain event that caused this entry, matching ledger_mapping_rules.source_event.Stored on the entry and not just inferred from the mapping, because one business factcan produce several entries - a sale posts revenue, then VAT, then cost of sales - andidempotency has to tell those apart.");

            migrationBuilder.AlterColumn<Guid>(
                name: "reversed_by_entry_id",
                table: "journal_entries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The entry that reverses this one. A posted entry is never edited or deleted - it isreversed.");

            migrationBuilder.AlterColumn<Guid>(
                name: "posted_by_user_id",
                table: "journal_entries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Who posted it.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "posted_at_utc",
                table: "journal_entries",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When the entry was posted. NULL while it is a draft.");

            migrationBuilder.AlterColumn<Guid>(
                name: "period_id",
                table: "journal_entries",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The accounting period this entry falls in.");

            migrationBuilder.AlterColumn<long>(
                name: "entry_no",
                table: "journal_entries",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "The gapless number of this entry within its period. Allocated from document_sequences,not from here.");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "entry_date",
                table: "journal_entries",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldComment: "The date the entry belongs to - the accounting period it lands in, not the moment it waswritten.");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "journal_entries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "What the entry is for, in the accountant's words.");
        }
    }
}
