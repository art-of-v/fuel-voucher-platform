using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheLastTablesDocumentThemselves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "verified_with_warnings_count",
                table: "voucher_imports",
                type: "integer",
                nullable: false,
                comment: "How many rows verified but with a warning - read and checked, with something odd that did notstop them.",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "verification_failed_count",
                table: "voucher_imports",
                type: "integer",
                nullable: false,
                comment: "How many rows were read but did not verify - the code does not match the fuel, the litres donot match the code.",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "voucher_imports",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                comment: "Started, Completed or Failed - the three values the importer writes. This status gates thewhole intake, so it is pinned by a CHECK rather than left as free text.",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<DateTime>(
                name: "started_at_utc",
                table: "voucher_imports",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the import began.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<int>(
                name: "page_count",
                table: "voucher_imports",
                type: "integer",
                nullable: false,
                comment: "How many pages the file had, as the importer counted them. The importer's own count, not thereader's.",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "imported_count",
                table: "voucher_imports",
                type: "integer",
                nullable: false,
                comment: "How many vouchers the import actually created.",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "file_name",
                table: "voucher_imports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "The file as it arrived - the name the operator's machine gave it. What the operatorrecognises, not a key.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<int>(
                name: "failed_count",
                table: "voucher_imports",
                type: "integer",
                nullable: false,
                comment: "How many rows could not be read at all.",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "duplicate_count",
                table: "voucher_imports",
                type: "integer",
                nullable: false,
                comment: "How many rows were already there - the same voucher seen twice. Not an error: a supplier'sfile often overlaps the previous one.",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<DateTime>(
                name: "completed_at_utc",
                table: "voucher_imports",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When the import finished - successfully or not. NULL while it is still running, and thedatabase ties this to the status below.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "surcharge_uah",
                table: "voucher_exchanges",
                type: "numeric(10,2)",
                nullable: false,
                comment: "This row's OWN share of the batch surcharge - the brand charge spread pro-rata over thispair's litres, NOT the batch total repeated. Do not read one row and call it the batchcharge: a four-voucher exchange would report a quarter of what was actually paid. Sum therows of an ExchangeBatchId.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)");

            migrationBuilder.AlterColumn<Guid>(
                name: "supplier_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: false,
                comment: "The supplier the old vouchers were bought from and the new ones were obtained from. Oneexchange involves exactly one supplier, so every row of a batch carries the same value - butit is recorded per row because this table is also read one voucher at a time during supplierreconciliation, where the row alone has to answer 'who do we settle with for this voucher'.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "old_voucher_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: false,
                comment: "The retired stock voucher.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "new_voucher_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: true,
                comment: "The new stock voucher this old one was replaced by, or NULL when the old voucher was notpaired - more old than new in its bucket.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "fuel_type_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: false,
                comment: "The fuel of the retired voucher, and of its paired replacement - the same bucket.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "exchange_batch_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: false,
                comment: "Groups every old voucher retired in one confirm action. The batch's lifecycle lives inmovement_batches, which this points at.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "voucher_exchanges",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the exchange was recorded.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<decimal>(
                name: "cost_per_liter_applied",
                table: "voucher_exchanges",
                type: "numeric(10,4)",
                nullable: true,
                comment: "The cost per litre set on the new fuel batch for this exchange (the old blended cost plus thesurcharge per litre), when there is one.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: false,
                comment: "The brand's id - the key the text above is a copy of. The composite foreign key(fuel_type_id, brand_id) -> fuel_types(id, brand_id) is what refuses a row whose brand isnot its fuel's brand.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "brand",
                table: "voucher_exchanges",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "The brand of the retired voucher (okko/wog/...), denormalized for audit.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "acting_user_name",
                table: "voucher_exchanges",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The admin's display name at the time, for the audit. The name is frozen, so it still readsright after the person is renamed or leaves.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "acting_user_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: false,
                comment: "The admin who recorded the exchange.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "vat_number",
                table: "operator_legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "The VAT number, if the company is a VAT payer. NULL is a real answer - most small companiesare not.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "operator_legal_entities",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it was last changed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "tax_regime",
                table: "operator_legal_entities",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                comment: "Which tax regime the company is on: VAT or simplified. The document that follows from this isa different document.",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "rnkrr",
                table: "operator_legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "The RNC RR - the record number in the unified state register - when it differs from theEDDRPOU. A sole trader has no EDDRPOU but does have an RNC RR.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "registered_address",
                table: "operator_legal_entities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "The legal address, as the registry has it. Not the address for delivery.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "operator_legal_entities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "The company's name as it appears on its own documents.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "legal_form",
                table: "operator_legal_entities",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                comment: "The legal form, e.g. TOV. Free text here, but a fiscal document must print one.",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "operator_legal_entities",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether fiscal documents may name this entity as their issuer. Seeded false; the row becomesusable only once someone confirms its EDDRPOU and VAT number are real.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "edrpou",
                table: "operator_legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "The EDDRPOU - the state registry number that identifies the company. It is the key, and thedatabase checks its shape.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "operator_legal_entities",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the row was written.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "tax_id",
                table: "natural_persons",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "IПН - the personal tax number. Nullable: most users never provide one, and a guessed tax idis worse than none.",
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
                comment: "The date of birth, if we were told it. Nullable - nobody is required to give it, and a guesseddate is worse than none.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true,
                oldComment: "The person's date of birth, when they gave it.");

            migrationBuilder.AlterColumn<Guid>(
                name: "party_id",
                table: "natural_persons",
                type: "uuid",
                nullable: false,
                comment: "The party these details belong to. A natural person IS a party: the party is the person, andthis row is what we know about them.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "vat_number",
                table: "legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "The VAT number, if the company is a VAT payer. NULL is a real answer.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "legal_entities",
                type: "uuid",
                nullable: false,
                comment: "The user who owns this company.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "legal_entities",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                comment: "When it was last changed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "The contact phone.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "legal_entities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "The company's name.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "legal_entities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The contact email.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "edrpou",
                table: "legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "The EDDRPOU - the state registry number that identifies the company.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "director_name",
                table: "legal_entities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The director's name, as the documents name them.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "legal_entities",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                comment: "When the row was written.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<string>(
                name: "address",
                table: "legal_entities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "The company's address.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "source_type",
                table: "ledger_mapping_rules",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                comment: "What kind of fact this rule maps: a payment, a refund, a fulfilment. The rule is matched onthis together with the event below.",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);

            migrationBuilder.AlterColumn<string>(
                name: "source_event",
                table: "ledger_mapping_rules",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                comment: "The domain event this rule answers to, e.g. order.fulfilled. It is the key the mapping hangson - a business fact without a rule is a fact that never reaches the books.",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "side",
                table: "ledger_mapping_rules",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                comment: "Whether the fact debits or credits this account. A rule states one side; the other side is asecond rule, and the two together are what balance.",
                oldClrType: typeof(string),
                oldType: "character varying(6)",
                oldMaxLength: 6);

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "ledger_mapping_rules",
                type: "uuid",
                nullable: false,
                comment: "The account the fact is posted to. One account per side per event, which is what makes themapping total rather than a preference.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "document_sequences",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "The identity of the sequence, e.g. journal:<period id>. Unique - two rows sharing it are twocounters racing for the same numbers, which is what this table exists to prevent.",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<long>(
                name: "last_value",
                table: "document_sequences",
                type: "bigint",
                nullable: false,
                comment: "The last number handed out. Starts at 0, so the first call returns 1.",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<Guid>(
                name: "operator_legal_entity_id",
                table: "bank_accounts",
                type: "uuid",
                nullable: false,
                comment: "The operator entity that owns this account. An account belongs to one issuer, and an issuer'sfiscal documents print it.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<bool>(
                name: "is_default",
                table: "bank_accounts",
                type: "boolean",
                nullable: false,
                comment: "Whether this is the account to use when no other is named. At most one per entity, and thedatabase says so too.",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "bank_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                comment: "Whether the account may still be used. A closed account stays in the table, because a fiscaldocument printed in the past still names it.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<string>(
                name: "currency",
                table: "bank_accounts",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                comment: "The currency of the account. UAH only - the database says so, because a second currency herewould be a second money inside one ledger.",
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

            migrationBuilder.AlterColumn<string>(
                name: "bank_code",
                table: "bank_accounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "The bank's own code, as the bank writes it. Together with the account number it identifies theaccount, and the pair is unique.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "account_ref",
                table: "bank_accounts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "The account number at the bank.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "verified_with_warnings_count",
                table: "voucher_imports",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "How many rows verified but with a warning - read and checked, with something odd that did notstop them.");

            migrationBuilder.AlterColumn<int>(
                name: "verification_failed_count",
                table: "voucher_imports",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "How many rows were read but did not verify - the code does not match the fuel, the litres donot match the code.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "voucher_imports",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldComment: "Started, Completed or Failed - the three values the importer writes. This status gates thewhole intake, so it is pinned by a CHECK rather than left as free text.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "started_at_utc",
                table: "voucher_imports",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the import began.");

            migrationBuilder.AlterColumn<int>(
                name: "page_count",
                table: "voucher_imports",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "How many pages the file had, as the importer counted them. The importer's own count, not thereader's.");

            migrationBuilder.AlterColumn<int>(
                name: "imported_count",
                table: "voucher_imports",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "How many vouchers the import actually created.");

            migrationBuilder.AlterColumn<string>(
                name: "file_name",
                table: "voucher_imports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "The file as it arrived - the name the operator's machine gave it. What the operatorrecognises, not a key.");

            migrationBuilder.AlterColumn<int>(
                name: "failed_count",
                table: "voucher_imports",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "How many rows could not be read at all.");

            migrationBuilder.AlterColumn<int>(
                name: "duplicate_count",
                table: "voucher_imports",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "How many rows were already there - the same voucher seen twice. Not an error: a supplier'sfile often overlaps the previous one.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "completed_at_utc",
                table: "voucher_imports",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When the import finished - successfully or not. NULL while it is still running, and thedatabase ties this to the status below.");

            migrationBuilder.AlterColumn<decimal>(
                name: "surcharge_uah",
                table: "voucher_exchanges",
                type: "numeric(10,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldComment: "This row's OWN share of the batch surcharge - the brand charge spread pro-rata over thispair's litres, NOT the batch total repeated. Do not read one row and call it the batchcharge: a four-voucher exchange would report a quarter of what was actually paid. Sum therows of an ExchangeBatchId.");

            migrationBuilder.AlterColumn<Guid>(
                name: "supplier_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The supplier the old vouchers were bought from and the new ones were obtained from. Oneexchange involves exactly one supplier, so every row of a batch carries the same value - butit is recorded per row because this table is also read one voucher at a time during supplierreconciliation, where the row alone has to answer 'who do we settle with for this voucher'.");

            migrationBuilder.AlterColumn<Guid>(
                name: "old_voucher_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The retired stock voucher.");

            migrationBuilder.AlterColumn<Guid>(
                name: "new_voucher_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The new stock voucher this old one was replaced by, or NULL when the old voucher was notpaired - more old than new in its bucket.");

            migrationBuilder.AlterColumn<Guid>(
                name: "fuel_type_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The fuel of the retired voucher, and of its paired replacement - the same bucket.");

            migrationBuilder.AlterColumn<Guid>(
                name: "exchange_batch_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Groups every old voucher retired in one confirm action. The batch's lifecycle lives inmovement_batches, which this points at.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "voucher_exchanges",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the exchange was recorded.");

            migrationBuilder.AlterColumn<decimal>(
                name: "cost_per_liter_applied",
                table: "voucher_exchanges",
                type: "numeric(10,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "The cost per litre set on the new fuel batch for this exchange (the old blended cost plus thesurcharge per litre), when there is one.");

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The brand's id - the key the text above is a copy of. The composite foreign key(fuel_type_id, brand_id) -> fuel_types(id, brand_id) is what refuses a row whose brand isnot its fuel's brand.");

            migrationBuilder.AlterColumn<string>(
                name: "brand",
                table: "voucher_exchanges",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "The brand of the retired voucher (okko/wog/...), denormalized for audit.");

            migrationBuilder.AlterColumn<string>(
                name: "acting_user_name",
                table: "voucher_exchanges",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The admin's display name at the time, for the audit. The name is frozen, so it still readsright after the person is renamed or leaves.");

            migrationBuilder.AlterColumn<Guid>(
                name: "acting_user_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The admin who recorded the exchange.");

            migrationBuilder.AlterColumn<string>(
                name: "vat_number",
                table: "operator_legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "The VAT number, if the company is a VAT payer. NULL is a real answer - most small companiesare not.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "operator_legal_entities",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it was last changed.");

            migrationBuilder.AlterColumn<string>(
                name: "tax_regime",
                table: "operator_legal_entities",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldComment: "Which tax regime the company is on: VAT or simplified. The document that follows from this isa different document.");

            migrationBuilder.AlterColumn<string>(
                name: "rnkrr",
                table: "operator_legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "The RNC RR - the record number in the unified state register - when it differs from theEDDRPOU. A sole trader has no EDDRPOU but does have an RNC RR.");

            migrationBuilder.AlterColumn<string>(
                name: "registered_address",
                table: "operator_legal_entities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "The legal address, as the registry has it. Not the address for delivery.");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "operator_legal_entities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "The company's name as it appears on its own documents.");

            migrationBuilder.AlterColumn<string>(
                name: "legal_form",
                table: "operator_legal_entities",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true,
                oldComment: "The legal form, e.g. TOV. Free text here, but a fiscal document must print one.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "operator_legal_entities",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false,
                oldComment: "Whether fiscal documents may name this entity as their issuer. Seeded false; the row becomesusable only once someone confirms its EDDRPOU and VAT number are real.");

            migrationBuilder.AlterColumn<string>(
                name: "edrpou",
                table: "operator_legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "The EDDRPOU - the state registry number that identifies the company. It is the key, and thedatabase checks its shape.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "operator_legal_entities",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the row was written.");

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
                oldNullable: true,
                oldComment: "IПН - the personal tax number. Nullable: most users never provide one, and a guessed tax idis worse than none.");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "birthdate",
                table: "natural_persons",
                type: "date",
                nullable: true,
                comment: "The person's date of birth, when they gave it.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true,
                oldComment: "The date of birth, if we were told it. Nullable - nobody is required to give it, and a guesseddate is worse than none.");

            migrationBuilder.AlterColumn<Guid>(
                name: "party_id",
                table: "natural_persons",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The party these details belong to. A natural person IS a party: the party is the person, andthis row is what we know about them.");

            migrationBuilder.AlterColumn<string>(
                name: "vat_number",
                table: "legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "The VAT number, if the company is a VAT payer. NULL is a real answer.");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "legal_entities",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The user who owns this company.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "legal_entities",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()",
                oldComment: "When it was last changed.");

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "The contact phone.");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "legal_entities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "The company's name.");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "legal_entities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The contact email.");

            migrationBuilder.AlterColumn<string>(
                name: "edrpou",
                table: "legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "The EDDRPOU - the state registry number that identifies the company.");

            migrationBuilder.AlterColumn<string>(
                name: "director_name",
                table: "legal_entities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The director's name, as the documents name them.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "legal_entities",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()",
                oldComment: "When the row was written.");

            migrationBuilder.AlterColumn<string>(
                name: "address",
                table: "legal_entities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "The company's address.");

            migrationBuilder.AlterColumn<string>(
                name: "source_type",
                table: "ledger_mapping_rules",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldComment: "What kind of fact this rule maps: a payment, a refund, a fulfilment. The rule is matched onthis together with the event below.");

            migrationBuilder.AlterColumn<string>(
                name: "source_event",
                table: "ledger_mapping_rules",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldComment: "The domain event this rule answers to, e.g. order.fulfilled. It is the key the mapping hangson - a business fact without a rule is a fact that never reaches the books.");

            migrationBuilder.AlterColumn<string>(
                name: "side",
                table: "ledger_mapping_rules",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(6)",
                oldMaxLength: 6,
                oldComment: "Whether the fact debits or credits this account. A rule states one side; the other side is asecond rule, and the two together are what balance.");

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "ledger_mapping_rules",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The account the fact is posted to. One account per side per event, which is what makes themapping total rather than a preference.");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "document_sequences",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "The identity of the sequence, e.g. journal:<period id>. Unique - two rows sharing it are twocounters racing for the same numbers, which is what this table exists to prevent.");

            migrationBuilder.AlterColumn<long>(
                name: "last_value",
                table: "document_sequences",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "The last number handed out. Starts at 0, so the first call returns 1.");

            migrationBuilder.AlterColumn<Guid>(
                name: "operator_legal_entity_id",
                table: "bank_accounts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The operator entity that owns this account. An account belongs to one issuer, and an issuer'sfiscal documents print it.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_default",
                table: "bank_accounts",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Whether this is the account to use when no other is named. At most one per entity, and thedatabase says so too.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "bank_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true,
                oldComment: "Whether the account may still be used. A closed account stays in the table, because a fiscaldocument printed in the past still names it.");

            migrationBuilder.AlterColumn<string>(
                name: "currency",
                table: "bank_accounts",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldComment: "The currency of the account. UAH only - the database says so, because a second currency herewould be a second money inside one ledger.");

            migrationBuilder.AlterColumn<string>(
                name: "bank_code",
                table: "bank_accounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "The bank's own code, as the bank writes it. Together with the account number it identifies theaccount, and the pair is unique.");

            migrationBuilder.AlterColumn<string>(
                name: "account_ref",
                table: "bank_accounts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "The account number at the bank.");
        }
    }
}
