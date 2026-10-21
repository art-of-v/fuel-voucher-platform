using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheLedgerDocumentsItself : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "voucher_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: false,
                comment: "The piece of paper that moved.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "to_location",
                table: "voucher_movements",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                comment: "Where it is now. Never equal to from_location - that isck_voucher_movements_distinct_locations, and a row that says a paper moved from theshelf to the shelf is not a movement.",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                table: "voucher_movements",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Why, in the operator's words.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: true,
                comment: "The order this movement settled, when a customer paid for it. A bare uuid, no foreignkey: an audit row must not be the thing that blocks deleting its own subject.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "occurred_at_utc",
                table: "voucher_movements",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it happened.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "line_item_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: true,
                comment: "The order line this movement settled, for the renewal paths.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "voucher_movements",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                comment: "What happened - issued, redeemed, blocked, released, and the rest. SeeVoucherMovementKinds.",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);

            migrationBuilder.AlterColumn<string>(
                name: "from_location",
                table: "voucher_movements",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                comment: "Where it was. See VoucherLocations.",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<decimal>(
                name: "cost_delta_uah",
                table: "voucher_movements",
                type: "numeric(14,2)",
                nullable: true,
                comment: "What this movement did to supplier-side cost, in UAH. NULL when cost did not move.Recorded AT THE TIME, from the values written in the same transaction, because it cannotbe derived afterwards: fuel_vouchers.cost_per_liter is mutable and gets overwritten, so'what did this voucher cost before' stops existing the moment the column is restamped.",
                oldClrType: typeof(decimal),
                oldType: "numeric(14,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "batch_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: true,
                comment: "The settlement this movement is part of, when there was one. NULL for a movement anoperator or a customer performed on its own.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_uah",
                table: "voucher_movements",
                type: "numeric(12,2)",
                nullable: true,
                comment: "What the customer paid for this movement, in UAH. NULL when money did not move - mostmovements are the operator's own housekeeping. NOT the batch total: a settlement's totallives on movement_batches.surcharge_uah.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: true,
                comment: "The person who caused it, required when actor_kind is 'user'. A bare uuid, no foreignkey - the reason every other audit record in this schema gives.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_party_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: true,
                comment: "The company that caused the movement, when it was a company rather than a user. A bareuuid.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "actor_kind",
                table: "voucher_movements",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "system",
                comment: "Who caused it: a person, or the scheduler. See VoucherActorKinds.",
                oldClrType: typeof(string),
                oldType: "character varying(12)",
                oldMaxLength: 12,
                oldDefaultValue: "system");

            migrationBuilder.AlterColumn<Guid>(
                name: "voucher_id",
                table: "journal_lines",
                type: "uuid",
                nullable: true,
                comment: "The voucher the line is about, where the line is about one.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "quantity_liters",
                table: "journal_lines",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true,
                comment: "The litres the line moves, for the subledger. NULL when the line moves no fuel.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldPrecision: 10,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "party_id",
                table: "journal_lines",
                type: "uuid",
                nullable: true,
                comment: "The counterparty, for the subledger. NULL when the posting has no counterparty.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<short>(
                name: "line_no",
                table: "journal_lines",
                type: "smallint",
                nullable: false,
                comment: "The line's number within its entry.",
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AlterColumn<Guid>(
                name: "legal_entity_id",
                table: "journal_lines",
                type: "uuid",
                nullable: true,
                comment: "The company the line belongs to. Keyed on id until D-02 re-keys it to party_id.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "entry_id",
                table: "journal_lines",
                type: "uuid",
                nullable: false,
                comment: "The entry this line belongs to.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<decimal>(
                name: "debit_uah",
                table: "journal_lines",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                comment: "The amount this line debits, in UAH.",
                oldClrType: typeof(decimal),
                oldType: "numeric(14,2)",
                oldPrecision: 14,
                oldScale: 2,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "credit_uah",
                table: "journal_lines",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                comment: "The amount this line credits, in UAH.",
                oldClrType: typeof(decimal),
                oldType: "numeric(14,2)",
                oldPrecision: 14,
                oldScale: 2,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "journal_lines",
                type: "uuid",
                nullable: false,
                comment: "The account this line posts to.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "parent_id",
                table: "chart_of_accounts",
                type: "uuid",
                nullable: true,
                comment: "The account this one rolls up into.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "normal_balance",
                table: "chart_of_accounts",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                comment: "Which side the account normally carries: a debit or a credit.",
                oldClrType: typeof(string),
                oldType: "character varying(6)",
                oldMaxLength: 6);

            migrationBuilder.AlterColumn<string>(
                name: "name_ukr",
                table: "chart_of_accounts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "The account's name, in Ukrainian.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<bool>(
                name: "is_postable",
                table: "chart_of_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                comment: "Whether entries may post to this account.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "chart_of_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                comment: "Whether the account is in use. A retired account stays, because entries point at it.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "chart_of_accounts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                comment: "The account number as the accountant writes it, e.g. 7911.",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "account_type",
                table: "chart_of_accounts",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                comment: "What kind of account this is: an asset, a liability, income or expense.",
                oldClrType: typeof(string),
                oldType: "character varying(12)",
                oldMaxLength: 12);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "accounting_periods",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Open",
                comment: "Whether the period is open or closed.",
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldDefaultValue: "Open");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "period_start",
                table: "accounting_periods",
                type: "date",
                nullable: false,
                comment: "The first day of the period.",
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "period_end",
                table: "accounting_periods",
                type: "date",
                nullable: false,
                comment: "The last day of the period.",
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<Guid>(
                name: "closed_by_user_id",
                table: "accounting_periods",
                type: "uuid",
                nullable: true,
                comment: "Who closed it.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "closed_at_utc",
                table: "accounting_periods",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When the period was closed. NULL while it is open.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "voucher_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The piece of paper that moved.");

            migrationBuilder.AlterColumn<string>(
                name: "to_location",
                table: "voucher_movements",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldComment: "Where it is now. Never equal to from_location - that isck_voucher_movements_distinct_locations, and a row that says a paper moved from theshelf to the shelf is not a movement.");

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                table: "voucher_movements",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "Why, in the operator's words.");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The order this movement settled, when a customer paid for it. A bare uuid, no foreignkey: an audit row must not be the thing that blocks deleting its own subject.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "occurred_at_utc",
                table: "voucher_movements",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it happened.");

            migrationBuilder.AlterColumn<Guid>(
                name: "line_item_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The order line this movement settled, for the renewal paths.");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "voucher_movements",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldComment: "What happened - issued, redeemed, blocked, released, and the rest. SeeVoucherMovementKinds.");

            migrationBuilder.AlterColumn<string>(
                name: "from_location",
                table: "voucher_movements",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldComment: "Where it was. See VoucherLocations.");

            migrationBuilder.AlterColumn<decimal>(
                name: "cost_delta_uah",
                table: "voucher_movements",
                type: "numeric(14,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,2)",
                oldNullable: true,
                oldComment: "What this movement did to supplier-side cost, in UAH. NULL when cost did not move.Recorded AT THE TIME, from the values written in the same transaction, because it cannotbe derived afterwards: fuel_vouchers.cost_per_liter is mutable and gets overwritten, so'what did this voucher cost before' stops existing the moment the column is restamped.");

            migrationBuilder.AlterColumn<Guid>(
                name: "batch_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The settlement this movement is part of, when there was one. NULL for a movement anoperator or a customer performed on its own.");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_uah",
                table: "voucher_movements",
                type: "numeric(12,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true,
                oldComment: "What the customer paid for this movement, in UAH. NULL when money did not move - mostmovements are the operator's own housekeeping. NOT the batch total: a settlement's totallives on movement_batches.surcharge_uah.");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The person who caused it, required when actor_kind is 'user'. A bare uuid, no foreignkey - the reason every other audit record in this schema gives.");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_party_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The company that caused the movement, when it was a company rather than a user. A bareuuid.");

            migrationBuilder.AlterColumn<string>(
                name: "actor_kind",
                table: "voucher_movements",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "system",
                oldClrType: typeof(string),
                oldType: "character varying(12)",
                oldMaxLength: 12,
                oldDefaultValue: "system",
                oldComment: "Who caused it: a person, or the scheduler. See VoucherActorKinds.");

            migrationBuilder.AlterColumn<Guid>(
                name: "voucher_id",
                table: "journal_lines",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The voucher the line is about, where the line is about one.");

            migrationBuilder.AlterColumn<decimal>(
                name: "quantity_liters",
                table: "journal_lines",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldPrecision: 10,
                oldScale: 2,
                oldNullable: true,
                oldComment: "The litres the line moves, for the subledger. NULL when the line moves no fuel.");

            migrationBuilder.AlterColumn<Guid>(
                name: "party_id",
                table: "journal_lines",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The counterparty, for the subledger. NULL when the posting has no counterparty.");

            migrationBuilder.AlterColumn<short>(
                name: "line_no",
                table: "journal_lines",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldComment: "The line's number within its entry.");

            migrationBuilder.AlterColumn<Guid>(
                name: "legal_entity_id",
                table: "journal_lines",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The company the line belongs to. Keyed on id until D-02 re-keys it to party_id.");

            migrationBuilder.AlterColumn<Guid>(
                name: "entry_id",
                table: "journal_lines",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The entry this line belongs to.");

            migrationBuilder.AlterColumn<decimal>(
                name: "debit_uah",
                table: "journal_lines",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,2)",
                oldPrecision: 14,
                oldScale: 2,
                oldDefaultValue: 0m,
                oldComment: "The amount this line debits, in UAH.");

            migrationBuilder.AlterColumn<decimal>(
                name: "credit_uah",
                table: "journal_lines",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,2)",
                oldPrecision: 14,
                oldScale: 2,
                oldDefaultValue: 0m,
                oldComment: "The amount this line credits, in UAH.");

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "journal_lines",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The account this line posts to.");

            migrationBuilder.AlterColumn<Guid>(
                name: "parent_id",
                table: "chart_of_accounts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The account this one rolls up into.");

            migrationBuilder.AlterColumn<string>(
                name: "normal_balance",
                table: "chart_of_accounts",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(6)",
                oldMaxLength: 6,
                oldComment: "Which side the account normally carries: a debit or a credit.");

            migrationBuilder.AlterColumn<string>(
                name: "name_ukr",
                table: "chart_of_accounts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "The account's name, in Ukrainian.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_postable",
                table: "chart_of_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true,
                oldComment: "Whether entries may post to this account.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "chart_of_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true,
                oldComment: "Whether the account is in use. A retired account stays, because entries point at it.");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "chart_of_accounts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldComment: "The account number as the accountant writes it, e.g. 7911.");

            migrationBuilder.AlterColumn<string>(
                name: "account_type",
                table: "chart_of_accounts",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(12)",
                oldMaxLength: 12,
                oldComment: "What kind of account this is: an asset, a liability, income or expense.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "accounting_periods",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Open",
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldDefaultValue: "Open",
                oldComment: "Whether the period is open or closed.");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "period_start",
                table: "accounting_periods",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldComment: "The first day of the period.");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "period_end",
                table: "accounting_periods",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldComment: "The last day of the period.");

            migrationBuilder.AlterColumn<Guid>(
                name: "closed_by_user_id",
                table: "accounting_periods",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Who closed it.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "closed_at_utc",
                table: "accounting_periods",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When the period was closed. NULL while it is open.");
        }
    }
}
