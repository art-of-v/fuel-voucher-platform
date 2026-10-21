using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheLastColumnsDocumentThemselves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "voucher_term_pricing",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When it was last changed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "voucher_term_pricing",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the row was written.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<int>(
                name: "token_version",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "The version of the tokens this account's sessions carry. Bumping it invalidates everyrefresh token at once - which is what a password change or a ban has to do, and what nosingle-token revocation can do.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<string>(
                name: "pending_email_token_hash",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "The HASH of the confirmation token, never the token itself: a database that is read bysomebody must not be able to confirm an address.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "pending_email_expires_at_utc",
                table: "users",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When the pending address stops being claimable. The token is short-lived on purpose - aconfirmation link that works for a year is a key.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "pending_email",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The address waiting to be confirmed, while the current one still works. NULL when nothingis pending.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_qa_account",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether this is a test account. The QA identity is explicitly distinguishable from a realcustomer, and the reports that count customers leave it out.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "is_deleted",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Soft delete. The user stays in the schema, because the orders, the money and the vouchersbehind them have to stay readable.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "is_banned",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether the account is banned. Separate from being deleted: a banned account keeps itshistory and cannot sign in.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether the account may sign in. Separate from being banned: an inactive account is one thecustomer closed, a banned one is one we closed.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<decimal>(
                name: "bonus_balance",
                table: "users",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m,
                comment: "The bonus balance in UAH - the money the platform owes the customer. A balance, not a price:it is what is LEFT, and it can be negative when a company has used more than it has paid.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "settlement_messages",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the row was written.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<bool>(
                name: "is_default",
                table: "roles",
                type: "boolean",
                nullable: false,
                comment: "Whether a new user gets this role when nobody says otherwise.",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "payments",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it was last changed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "merchant",
                table: "payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "Which acquirer took the money. The word is ours and the list is ours too - this is amerchant WE chose, which is why this one is constrained and the acquirer's own statusesare not.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "payments",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the row was written.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                table: "order_line_items",
                type: "uuid",
                nullable: false,
                comment: "The order this line belongs to.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "fuel_type_id",
                table: "order_line_items",
                type: "uuid",
                nullable: false,
                comment: "The fuel this line is for.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_seen_at_utc",
                table: "monobank_statement_entries",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When we last saw it in a fetch. A row that keeps being re-reported is one the acquirer isstill revising, which is different from one that is settled and closed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_user_id",
                table: "journal_entries",
                type: "uuid",
                nullable: true,
                comment: "Who wrote it. Distinct from who posted it: a draft has a writer and no poster.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "journal_entries",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the entry was WRITTEN - which is not when it was posted: a draft can sit for days.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "fuel_vouchers",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it was last changed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "qr_parameters_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                comment: "The QR parameters read from the original code - the geometry needed to regenerate it. NULLwhen the code was not read.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "legal_entity_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                comment: "The company the voucher was sold to.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "fuel_type_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: false,
                comment: "The fuel this voucher is for.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "fuel_vouchers",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the row was written.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "acquisition_source",
                table: "fuel_vouchers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "direct",
                comment: "The supply route this voucher came in on: direct, or through a partner. Immutable, andenforced by a trigger: the cost of a voucher is a function of where it came from, so anedited value does not merely relabel a row - it moves real money between two reports thatare supposed to be separate.",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "direct");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "voucher_term_pricing",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When it was last changed.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "voucher_term_pricing",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the row was written.");

            migrationBuilder.AlterColumn<int>(
                name: "token_version",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1,
                oldComment: "The version of the tokens this account's sessions carry. Bumping it invalidates everyrefresh token at once - which is what a password change or a ban has to do, and what nosingle-token revocation can do.");

            migrationBuilder.AlterColumn<string>(
                name: "pending_email_token_hash",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "The HASH of the confirmation token, never the token itself: a database that is read bysomebody must not be able to confirm an address.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "pending_email_expires_at_utc",
                table: "users",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When the pending address stops being claimable. The token is short-lived on purpose - aconfirmation link that works for a year is a key.");

            migrationBuilder.AlterColumn<string>(
                name: "pending_email",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The address waiting to be confirmed, while the current one still works. NULL when nothingis pending.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_qa_account",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false,
                oldComment: "Whether this is a test account. The QA identity is explicitly distinguishable from a realcustomer, and the reports that count customers leave it out.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_deleted",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false,
                oldComment: "Soft delete. The user stays in the schema, because the orders, the money and the vouchersbehind them have to stay readable.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_banned",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false,
                oldComment: "Whether the account is banned. Separate from being deleted: a banned account keeps itshistory and cannot sign in.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false,
                oldComment: "Whether the account may sign in. Separate from being banned: an inactive account is one thecustomer closed, a banned one is one we closed.");

            migrationBuilder.AlterColumn<decimal>(
                name: "bonus_balance",
                table: "users",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldDefaultValue: 0m,
                oldComment: "The bonus balance in UAH - the money the platform owes the customer. A balance, not a price:it is what is LEFT, and it can be negative when a company has used more than it has paid.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "settlement_messages",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the row was written.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_default",
                table: "roles",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Whether a new user gets this role when nobody says otherwise.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "payments",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it was last changed.");

            migrationBuilder.AlterColumn<string>(
                name: "merchant",
                table: "payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "Which acquirer took the money. The word is ours and the list is ours too - this is amerchant WE chose, which is why this one is constrained and the acquirer's own statusesare not.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "payments",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the row was written.");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                table: "order_line_items",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The order this line belongs to.");

            migrationBuilder.AlterColumn<Guid>(
                name: "fuel_type_id",
                table: "order_line_items",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The fuel this line is for.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_seen_at_utc",
                table: "monobank_statement_entries",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When we last saw it in a fetch. A row that keeps being re-reported is one the acquirer isstill revising, which is different from one that is settled and closed.");

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_user_id",
                table: "journal_entries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Who wrote it. Distinct from who posted it: a draft has a writer and no poster.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "journal_entries",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the entry was WRITTEN - which is not when it was posted: a draft can sit for days.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "fuel_vouchers",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it was last changed.");

            migrationBuilder.AlterColumn<Guid>(
                name: "qr_parameters_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The QR parameters read from the original code - the geometry needed to regenerate it. NULLwhen the code was not read.");

            migrationBuilder.AlterColumn<Guid>(
                name: "legal_entity_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The company the voucher was sold to.");

            migrationBuilder.AlterColumn<Guid>(
                name: "fuel_type_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The fuel this voucher is for.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "fuel_vouchers",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the row was written.");

            migrationBuilder.AlterColumn<string>(
                name: "acquisition_source",
                table: "fuel_vouchers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "direct",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "direct",
                oldComment: "The supply route this voucher came in on: direct, or through a partner. Immutable, andenforced by a trigger: the cost of a voucher is a function of where it came from, so anedited value does not merely relabel a row - it moves real money between two reports thatare supposed to be separate.");
        }
    }
}
