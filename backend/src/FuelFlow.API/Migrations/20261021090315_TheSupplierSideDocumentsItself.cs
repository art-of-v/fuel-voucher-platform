using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheSupplierSideDocumentsItself : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "voucher_number",
                table: "voucher_import_errors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "The voucher number as it was read, if it could be read at all. NULL when the line wasunreadable - which is often exactly why it is an error.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "raw_text",
                table: "voucher_import_errors",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                comment: "The line as it arrived, exactly as the file had it. The evidence: without it, nobody cantell whether the importer read the file wrong or the file was wrong.",
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "page_number",
                table: "voucher_import_errors",
                type: "integer",
                nullable: false,
                comment: "The page of the file the row was read from. The page, not the row: a page is what a personcan look at.",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "import_id",
                table: "voucher_import_errors",
                type: "uuid",
                nullable: false,
                comment: "The import this error belongs to. Every failed row points at the run that produced it, soone file's failures are read together.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "error_message",
                table: "voucher_import_errors",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                comment: "What went wrong, in words. The reason the row was refused.",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "voucher_import_errors",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the row was written.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "user_contracts",
                type: "uuid",
                nullable: false,
                comment: "The user who signed.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "signed_at_utc",
                table: "user_contracts",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it was signed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "signature_data",
                table: "user_contracts",
                type: "text",
                nullable: true,
                comment: "The signature itself, as it was captured. NULL for a contract signed on paper and keyed inby hand - which is a real answer, not a missing value.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "contract_id",
                table: "user_contracts",
                type: "uuid",
                nullable: false,
                comment: "The contract that was signed.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "suppliers",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it was last changed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "rnkrr",
                table: "suppliers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "РНОКР - the tax register number. Individuals have one too; companies may omit it.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "suppliers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                comment: "The phone to call about this supplier.",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "suppliers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                comment: "Free-form: settlement terms, who to call, what the invoice must include.",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "suppliers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "The supplier's display name. Unique among suppliers - two rows with the same name are onecounterparty recorded twice.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "legal_form",
                table: "suppliers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                comment: "ФОП / ТОВ / ТзОВ / ФОП-ЄЗ / інше. Which form it is decides which requisites aremeaningful.",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "suppliers",
                type: "boolean",
                nullable: false,
                comment: "Inactive suppliers stay on past vouchers for audit, but they are not offered to theoperator.",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "suppliers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "The address to write to.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "edr_ipn",
                table: "suppliers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "The registry code - ЄДРОПУ for a company, ІПН for an individual. Optional, and uniquewhen it is there: two rows with the same code are the same counterparty recorded twice, anda business that pays two suppliers who are one person pays twice and reconciles thedifference by hand.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "suppliers",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the row was written.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "address",
                table: "suppliers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "The supplier's address.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "acquisition_source",
                table: "suppliers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "direct",
                comment: "Which supply route this supplier sells on: direct, or through a partner. Immutable - asupplier that moves between routes is a different supply relationship, and a voucher boughtlast quarter has to keep reporting in the bucket it was bought into.",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "direct");

            migrationBuilder.AlterColumn<int>(
                name: "version",
                table: "qr_parameters",
                type: "integer",
                nullable: true,
                comment: "The QR code version (1-40), read from the aligned bit-matrix width. It controls the griddensity.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "mask_pattern",
                table: "qr_parameters",
                type: "integer",
                nullable: true,
                comment: "The data-mask reference number (0-7), read from the format-information strip.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "encoding_mode",
                table: "qr_parameters",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                comment: "The data-encoding mode of the original QR: NUMERIC, ALPHANUMERIC or BYTE. A hint when thecode is regenerated.",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ecc_level",
                table: "qr_parameters",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                comment: "The error correction level of the original QR: L, M, Q or H.",
                oldClrType: typeof(string),
                oldType: "character varying(1)",
                oldMaxLength: 1);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "qr_parameters",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the row was written.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "purchase_batches",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it was last changed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "supplier_id",
                table: "purchase_batches",
                type: "uuid",
                nullable: false,
                comment: "The company this stock was bought from. Unlike the brand, this is the party we settle withand hand vouchers back to.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "import_job_id",
                table: "purchase_batches",
                type: "uuid",
                nullable: false,
                comment: "The import that brought this stock in.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "fuel_type_id",
                table: "purchase_batches",
                type: "uuid",
                nullable: false,
                comment: "The fuel this batch is.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "entered_by_user_id",
                table: "purchase_batches",
                type: "uuid",
                nullable: true,
                comment: "The user who entered the batch. NULL for a batch that came in through an import.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "purchase_batches",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the row was written.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "purchase_batches",
                type: "uuid",
                nullable: false,
                comment: "The brand's id - the key the text above is a copy of. The composite foreign key(fuel_type_id, brand_id) -> fuel_types(id, brand_id) is what refuses a row whose brand isnot its fuel's brand.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "brand",
                table: "purchase_batches",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "The brand's NAME - what a report groups by and what a parser reads. The id below is whatthe database can check, and the two are the same fact written twice.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "contracts",
                type: "uuid",
                nullable: false,
                comment: "The user who signs this contract.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "legal_entity_id",
                table: "contracts",
                type: "uuid",
                nullable: false,
                comment: "The company the contract is for.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "expires_at_utc",
                table: "contracts",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the contract stops being valid. Mandatory since 2026-10-20, on the business'sinstruction: a contract without a term is not a contract, it is a record that something oncehappened. The sign endpoint refuses to sign a contract that has already expired, so theterm is not a field nobody reads - it is the thing that stops a signature.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "contracts",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the contract was created.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "contracts",
                type: "uuid",
                nullable: false,
                comment: "The brand the contract covers - the terms of one brand, not of the platform.",
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "voucher_number",
                table: "voucher_import_errors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "The voucher number as it was read, if it could be read at all. NULL when the line wasunreadable - which is often exactly why it is an error.");

            migrationBuilder.AlterColumn<string>(
                name: "raw_text",
                table: "voucher_import_errors",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true,
                oldComment: "The line as it arrived, exactly as the file had it. The evidence: without it, nobody cantell whether the importer read the file wrong or the file was wrong.");

            migrationBuilder.AlterColumn<int>(
                name: "page_number",
                table: "voucher_import_errors",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "The page of the file the row was read from. The page, not the row: a page is what a personcan look at.");

            migrationBuilder.AlterColumn<Guid>(
                name: "import_id",
                table: "voucher_import_errors",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The import this error belongs to. Every failed row points at the run that produced it, soone file's failures are read together.");

            migrationBuilder.AlterColumn<string>(
                name: "error_message",
                table: "voucher_import_errors",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldComment: "What went wrong, in words. The reason the row was refused.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "voucher_import_errors",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the row was written.");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "user_contracts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The user who signed.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "signed_at_utc",
                table: "user_contracts",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it was signed.");

            migrationBuilder.AlterColumn<string>(
                name: "signature_data",
                table: "user_contracts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "The signature itself, as it was captured. NULL for a contract signed on paper and keyed inby hand - which is a real answer, not a missing value.");

            migrationBuilder.AlterColumn<Guid>(
                name: "contract_id",
                table: "user_contracts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The contract that was signed.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "suppliers",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it was last changed.");

            migrationBuilder.AlterColumn<string>(
                name: "rnkrr",
                table: "suppliers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "РНОКР - the tax register number. Individuals have one too; companies may omit it.");

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "suppliers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true,
                oldComment: "The phone to call about this supplier.");

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "suppliers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true,
                oldComment: "Free-form: settlement terms, who to call, what the invoice must include.");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "suppliers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "The supplier's display name. Unique among suppliers - two rows with the same name are onecounterparty recorded twice.");

            migrationBuilder.AlterColumn<string>(
                name: "legal_form",
                table: "suppliers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true,
                oldComment: "ФОП / ТОВ / ТзОВ / ФОП-ЄЗ / інше. Which form it is decides which requisites aremeaningful.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "suppliers",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Inactive suppliers stay on past vouchers for audit, but they are not offered to theoperator.");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "suppliers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "The address to write to.");

            migrationBuilder.AlterColumn<string>(
                name: "edr_ipn",
                table: "suppliers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "The registry code - ЄДРОПУ for a company, ІПН for an individual. Optional, and uniquewhen it is there: two rows with the same code are the same counterparty recorded twice, anda business that pays two suppliers who are one person pays twice and reconciles thedifference by hand.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "suppliers",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the row was written.");

            migrationBuilder.AlterColumn<string>(
                name: "address",
                table: "suppliers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "The supplier's address.");

            migrationBuilder.AlterColumn<string>(
                name: "acquisition_source",
                table: "suppliers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "direct",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "direct",
                oldComment: "Which supply route this supplier sells on: direct, or through a partner. Immutable - asupplier that moves between routes is a different supply relationship, and a voucher boughtlast quarter has to keep reporting in the bucket it was bought into.");

            migrationBuilder.AlterColumn<int>(
                name: "version",
                table: "qr_parameters",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "The QR code version (1-40), read from the aligned bit-matrix width. It controls the griddensity.");

            migrationBuilder.AlterColumn<int>(
                name: "mask_pattern",
                table: "qr_parameters",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "The data-mask reference number (0-7), read from the format-information strip.");

            migrationBuilder.AlterColumn<string>(
                name: "encoding_mode",
                table: "qr_parameters",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true,
                oldComment: "The data-encoding mode of the original QR: NUMERIC, ALPHANUMERIC or BYTE. A hint when thecode is regenerated.");

            migrationBuilder.AlterColumn<string>(
                name: "ecc_level",
                table: "qr_parameters",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1)",
                oldMaxLength: 1,
                oldComment: "The error correction level of the original QR: L, M, Q or H.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "qr_parameters",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the row was written.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "purchase_batches",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it was last changed.");

            migrationBuilder.AlterColumn<Guid>(
                name: "supplier_id",
                table: "purchase_batches",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The company this stock was bought from. Unlike the brand, this is the party we settle withand hand vouchers back to.");

            migrationBuilder.AlterColumn<Guid>(
                name: "import_job_id",
                table: "purchase_batches",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The import that brought this stock in.");

            migrationBuilder.AlterColumn<Guid>(
                name: "fuel_type_id",
                table: "purchase_batches",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The fuel this batch is.");

            migrationBuilder.AlterColumn<Guid>(
                name: "entered_by_user_id",
                table: "purchase_batches",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The user who entered the batch. NULL for a batch that came in through an import.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "purchase_batches",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the row was written.");

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "purchase_batches",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The brand's id - the key the text above is a copy of. The composite foreign key(fuel_type_id, brand_id) -> fuel_types(id, brand_id) is what refuses a row whose brand isnot its fuel's brand.");

            migrationBuilder.AlterColumn<string>(
                name: "brand",
                table: "purchase_batches",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "The brand's NAME - what a report groups by and what a parser reads. The id below is whatthe database can check, and the two are the same fact written twice.");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "contracts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The user who signs this contract.");

            migrationBuilder.AlterColumn<Guid>(
                name: "legal_entity_id",
                table: "contracts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The company the contract is for.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "expires_at_utc",
                table: "contracts",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the contract stops being valid. Mandatory since 2026-10-20, on the business'sinstruction: a contract without a term is not a contract, it is a record that something oncehappened. The sign endpoint refuses to sign a contract that has already expired, so theterm is not a field nobody reads - it is the thing that stops a signature.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "contracts",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the contract was created.");

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "contracts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The brand the contract covers - the terms of one brand, not of the platform.");
        }
    }
}
