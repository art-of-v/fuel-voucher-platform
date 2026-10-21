using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheAccountTablesDocumentThemselves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "used_at_utc",
                table: "verification_codes",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When it was redeemed, or NULL while it has not been.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone_number",
                table: "verification_codes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "The number the code was sent to.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<bool>(
                name: "is_used",
                table: "verification_codes",
                type: "boolean",
                nullable: false,
                comment: "Whether the code has been redeemed.",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<int>(
                name: "failed_attempts",
                table: "verification_codes",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "How many times it was tried and failed.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<DateTime>(
                name: "expires_at_utc",
                table: "verification_codes",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the code stops being valid.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "verification_codes",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it was sent.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "verification_codes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "The code itself, hashed.",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "users",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it last changed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "settlement_code",
                table: "users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true,
                comment: "The short identifier an operator quotes to the finance channel to attribute money tothis customer. Issued once by the database and immutable for the life of the account: achanged code orphans every payment recorded against it. Deliberately NOT the referralcode - that one is optional and may change, and a customer without one would have no wayto be paid on this route at all. It also avoids the 0/O/1/I/L pairs: it is transcribedby hand, and those are the pairs people misread. A mistyped code credits money to thewrong person, so the alphabet trades a little space for legibility. Staff-only, and itmust not appear in any customer-facing response.",
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "role_id",
                table: "users",
                type: "uuid",
                nullable: true,
                comment: "The role this account has, or NULL for an account with no role - which is a state theadmin can repair, not a state the database should refuse.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "referred_by",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Who referred this customer, in their own words.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "referral_code",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "The code the customer chose to be referred by. Optional, and it may change - correct formarketing, and deliberately NOT the same thing as the settlement code.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "profile_image_url",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Where the avatar is.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone_number",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "The customer's phone number, and their identity: the login is the number.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<Guid>(
                name: "party_id",
                table: "users",
                type: "uuid",
                nullable: true,
                comment: "The user's party record (D-02, additive step 1). NULLABLE on purpose: the documentedsequence is insert, switch, reconcile, then retire, and this column is the insert.Nothing reads it yet and no handler depends on it, so a user existing without one is notan error today - it becomes one when the column is promoted to NOT NULL, and that is aseparate migration with a test that says so.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "last_name",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "The customer's last name.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_login_at_utc",
                table: "users",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When it last signed in. NULL until it does.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "first_name",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "The customer's first name.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The customer's email, when they gave one.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "users",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the account was created.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "birthdate",
                table: "users",
                type: "date",
                nullable: true,
                comment: "The customer's date of birth, when they gave it.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "roles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "The role's name, and the value the application matches on.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<int>(
                name: "level",
                table: "roles",
                type: "integer",
                nullable: false,
                comment: "The role's rank: User 0, QA 5, Manager 10, Admin 50, ProductOwner 100. The hierarchy isthe number, so a role's place in it is arithmetic rather than a table of who outrankswhom.",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "roles",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the role was created.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                comment: "The account the token belongs to.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "token",
                table: "refresh_tokens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "The token itself, hashed. The plaintext exists only in the response that issued it.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "role_name_at_issue",
                table: "refresh_tokens",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "The role the account had WHEN the token was issued. The role is copied rather than readat use time, so that a role change does not silently change what an old token can do.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "revoked_at_utc",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When it was taken back, or NULL while it has not been.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_revoked",
                table: "refresh_tokens",
                type: "boolean",
                nullable: false,
                comment: "Whether the token has been taken back.",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<Guid>(
                name: "family_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                comment: "The family the token belongs to. Every token issued from one login shares a family, anda reuse of a revoked token revokes the whole family - that is how a stolen token isdetected rather than merely used once.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "expires_at_utc",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the token stops being valid.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                table: "refresh_tokens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The device the token was issued to, when it is known.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it was issued.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "party_type",
                table: "parties",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                comment: "What kind of party this is: a natural person or a company.",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                table: "parties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "The name this party is shown under. Denormalised on purpose - the ledger needs one nameto print, and a person and a company are named differently.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "parties",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the party record was created.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "tax_id",
                table: "natural_persons",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "The person's tax id (РНОКПП), when they gave it. A company cannot be invoiced withoutone, which is why a natural person without one is a gap in the data and not a defect inthe column.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "birthdate",
                table: "natural_persons",
                type: "date",
                nullable: true,
                comment: "The person's date of birth, when they gave it.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "devices",
                type: "uuid",
                nullable: false,
                comment: "The account this device belongs to.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "devices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Whether the device is trusted, blocked or pending.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "public_key",
                table: "devices",
                type: "text",
                nullable: false,
                comment: "The public key the device signs with. The private half never leaves the device.",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "os_version",
                table: "devices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                comment: "The operating system version, as the app reports it.",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_seen_at",
                table: "devices",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it was last seen.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "device_model",
                table: "devices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                comment: "The device model, as the app reports it.",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                table: "devices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                comment: "The device's own id, as the app reports it.",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                table: "devices",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the device was first seen.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "app_version",
                table: "devices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                comment: "The app version, as the app reports it.",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "used_at_utc",
                table: "verification_codes",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When it was redeemed, or NULL while it has not been.");

            migrationBuilder.AlterColumn<string>(
                name: "phone_number",
                table: "verification_codes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "The number the code was sent to.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_used",
                table: "verification_codes",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Whether the code has been redeemed.");

            migrationBuilder.AlterColumn<int>(
                name: "failed_attempts",
                table: "verification_codes",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0,
                oldComment: "How many times it was tried and failed.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "expires_at_utc",
                table: "verification_codes",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the code stops being valid.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "verification_codes",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it was sent.");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "verification_codes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "The code itself, hashed.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "users",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it last changed.");

            migrationBuilder.AlterColumn<string>(
                name: "settlement_code",
                table: "users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldNullable: true,
                oldComment: "The short identifier an operator quotes to the finance channel to attribute money tothis customer. Issued once by the database and immutable for the life of the account: achanged code orphans every payment recorded against it. Deliberately NOT the referralcode - that one is optional and may change, and a customer without one would have no wayto be paid on this route at all. It also avoids the 0/O/1/I/L pairs: it is transcribedby hand, and those are the pairs people misread. A mistyped code credits money to thewrong person, so the alphabet trades a little space for legibility. Staff-only, and itmust not appear in any customer-facing response.");

            migrationBuilder.AlterColumn<Guid>(
                name: "role_id",
                table: "users",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The role this account has, or NULL for an account with no role - which is a state theadmin can repair, not a state the database should refuse.");

            migrationBuilder.AlterColumn<string>(
                name: "referred_by",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldComment: "Who referred this customer, in their own words.");

            migrationBuilder.AlterColumn<string>(
                name: "referral_code",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldComment: "The code the customer chose to be referred by. Optional, and it may change - correct formarketing, and deliberately NOT the same thing as the settlement code.");

            migrationBuilder.AlterColumn<string>(
                name: "profile_image_url",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "Where the avatar is.");

            migrationBuilder.AlterColumn<string>(
                name: "phone_number",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "The customer's phone number, and their identity: the login is the number.");

            migrationBuilder.AlterColumn<Guid>(
                name: "party_id",
                table: "users",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The user's party record (D-02, additive step 1). NULLABLE on purpose: the documentedsequence is insert, switch, reconcile, then retire, and this column is the insert.Nothing reads it yet and no handler depends on it, so a user existing without one is notan error today - it becomes one when the column is promoted to NOT NULL, and that is aseparate migration with a test that says so.");

            migrationBuilder.AlterColumn<string>(
                name: "last_name",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "The customer's last name.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_login_at_utc",
                table: "users",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When it last signed in. NULL until it does.");

            migrationBuilder.AlterColumn<string>(
                name: "first_name",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "The customer's first name.");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The customer's email, when they gave one.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "users",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the account was created.");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "birthdate",
                table: "users",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true,
                oldComment: "The customer's date of birth, when they gave it.");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "roles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "The role's name, and the value the application matches on.");

            migrationBuilder.AlterColumn<int>(
                name: "level",
                table: "roles",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "The role's rank: User 0, QA 5, Manager 10, Admin 50, ProductOwner 100. The hierarchy isthe number, so a role's place in it is arithmetic rather than a table of who outrankswhom.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "roles",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the role was created.");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The account the token belongs to.");

            migrationBuilder.AlterColumn<string>(
                name: "token",
                table: "refresh_tokens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "The token itself, hashed. The plaintext exists only in the response that issued it.");

            migrationBuilder.AlterColumn<string>(
                name: "role_name_at_issue",
                table: "refresh_tokens",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldComment: "The role the account had WHEN the token was issued. The role is copied rather than readat use time, so that a role change does not silently change what an old token can do.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "revoked_at_utc",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When it was taken back, or NULL while it has not been.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_revoked",
                table: "refresh_tokens",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Whether the token has been taken back.");

            migrationBuilder.AlterColumn<Guid>(
                name: "family_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The family the token belongs to. Every token issued from one login shares a family, anda reuse of a revoked token revokes the whole family - that is how a stolen token isdetected rather than merely used once.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "expires_at_utc",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the token stops being valid.");

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                table: "refresh_tokens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The device the token was issued to, when it is known.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it was issued.");

            migrationBuilder.AlterColumn<string>(
                name: "party_type",
                table: "parties",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldComment: "What kind of party this is: a natural person or a company.");

            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                table: "parties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "The name this party is shown under. Denormalised on purpose - the ledger needs one nameto print, and a person and a company are named differently.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "parties",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the party record was created.");

            migrationBuilder.AlterColumn<string>(
                name: "tax_id",
                table: "natural_persons",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "The person's tax id (РНОКПП), when they gave it. A company cannot be invoiced withoutone, which is why a natural person without one is a gap in the data and not a defect inthe column.");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "birthdate",
                table: "natural_persons",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true,
                oldComment: "The person's date of birth, when they gave it.");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "devices",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The account this device belongs to.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "devices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Whether the device is trusted, blocked or pending.");

            migrationBuilder.AlterColumn<string>(
                name: "public_key",
                table: "devices",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "The public key the device signs with. The private half never leaves the device.");

            migrationBuilder.AlterColumn<string>(
                name: "os_version",
                table: "devices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true,
                oldComment: "The operating system version, as the app reports it.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_seen_at",
                table: "devices",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it was last seen.");

            migrationBuilder.AlterColumn<string>(
                name: "device_model",
                table: "devices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true,
                oldComment: "The device model, as the app reports it.");

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                table: "devices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldComment: "The device's own id, as the app reports it.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                table: "devices",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the device was first seen.");

            migrationBuilder.AlterColumn<string>(
                name: "app_version",
                table: "devices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true,
                oldComment: "The app version, as the app reports it.");
        }
    }
}
