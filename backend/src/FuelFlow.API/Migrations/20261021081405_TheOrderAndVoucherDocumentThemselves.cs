using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheOrderAndVoucherDocumentThemselves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "unit_price",
                table: "order_line_items",
                type: "numeric(12,2)",
                nullable: false,
                comment: "What the customer paid for ONE unit, in UAH.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.AlterColumn<string>(
                name: "term_code",
                table: "order_line_items",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true,
                comment: "The term the customer bought this fuel for, frozen at checkout. NULL means the fullremaining supplier term was sold. A renewal line ALWAYS has one.",
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "source_voucher_id",
                table: "order_line_items",
                type: "uuid",
                nullable: true,
                comment: "The voucher this line renews, when the line is a renewal; NULL on every other kind ofline. It lives on the LINE and not on the fulfilment: a renewal is an intent - it existsfrom checkout, before payment and before any stock is assigned - and a paid renewal thatfinds no stock has no fulfilment row at all.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "renewal_branch",
                table: "order_line_items",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                comment: "Which fulfilment path this renewal took: 'extend' or 'replace'. NULL on every other kindof line. Stored rather than derived, because the branch is DECIDED AND PRICED atcheckout from the voucher's state that day, while fulfilment deliberately re-decides itfrom the current state - the voucher can lapse in between - so nothing reading it backlater can recompute the original answer.",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "quantity",
                table: "order_line_items",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "How many units of this line the customer bought.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<string>(
                name: "pricing_basis",
                table: "order_line_items",
                type: "jsonb",
                nullable: true,
                comment: "The inputs that produced unit_price, frozen at checkout, as a JSON object: the per-litrecost, the margin, the pump price, the minimum discount and the terms id this line waspriced under. unit_price records WHAT the customer paid; this records WHY. NULL forevery order placed before the column existed, and deliberately not backfilled:backfilling from today's contract would manufacture a history that never happened, whichis worse than an honest gap - it looks like evidence.",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "previous_customer_expiration",
                table: "order_line_items",
                type: "date",
                nullable: true,
                comment: "The customer's expiry on the source voucher BEFORE this renewal was applied. NULL untilfulfilment, and on every non-renewal line. A snapshot, because there is nothing left toread it from afterwards: the extend branch overwritesfuel_vouchers.customer_expiration_date in place, and no history column survives it.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "original_line_total",
                table: "order_line_items",
                type: "numeric(12,2)",
                nullable: true,
                comment: "The pump-based LIST total for this line, in UAH, frozen at checkout. The customer'ssaving against the pump is original_line_total - line_total. NULL for rows predating thecolumn (no backfill - a historical pump price cannot be reconstructed) and for lineswith no pump reference (a voucher renewal), which therefore contribute no saving.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "new_customer_expiration",
                table: "order_line_items",
                type: "date",
                nullable: true,
                comment: "The expiry this renewal gave the customer. On a REPLACE this is the PROMISE, not thereplacement voucher's real life: what the customer bought is what they can rely on, andany longer real life on the stock voucher is a hidden reserve they may extend againlater - and showing it here would be selling twice.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "liters",
                table: "order_line_items",
                type: "numeric(10,2)",
                nullable: false,
                comment: "How many litres one unit of this line is.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "line_total",
                table: "order_line_items",
                type: "numeric(12,2)",
                nullable: false,
                comment: "What the customer paid for the WHOLE line, in UAH. This is the truth of the money;original_line_total is the reference it is measured against.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "order_line_items",
                type: "uuid",
                nullable: false,
                comment: "The brand's id - the key the text above is a copy of. The composite foreign key(fuel_type_id, brand_id) -> fuel_types(id, brand_id) is what refuses a row whose brandis not its fuel's brand. A nullable key would be a key the database does not check,because a foreign key with a NULL in it is not compared at all.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "brand",
                table: "order_line_items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "The brand's NAME - what a report groups by and what a parser reads. The id below is whatthe database can check, and the two are the same fact written twice.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<Guid>(
                name: "worker_user_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                comment: "The worker it was issued to.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "voucher_number",
                table: "fuel_vouchers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "The voucher's own number, as the supplier printed it.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<int>(
                name: "verification_total_modules",
                table: "fuel_vouchers",
                type: "integer",
                nullable: true,
                comment: "How many modules were checked.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "verification_mismatched_modules",
                table: "fuel_vouchers",
                type: "integer",
                nullable: true,
                comment: "How many modules disagreed.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "verification_mismatch_percent",
                table: "fuel_vouchers",
                type: "numeric(5,4)",
                nullable: true,
                comment: "How far the verification of this voucher's modules disagreed, as a percentage. NULL whenit was never verified.",
                oldClrType: typeof(decimal),
                oldType: "numeric(5,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "used_at_utc",
                table: "fuel_vouchers",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When this voucher was actually refuelled with. Set once, when it is marked used; nevermoved afterwards. NULL while the voucher is still unused.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "supplier_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                comment: "Who we bought it from.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "supplier_expiration_date",
                table: "fuel_vouchers",
                type: "date",
                nullable: false,
                comment: "The real term of the SUPPLIER's voucher, read verbatim from the printed document atimport. It is the hard ceiling on everything we may promise: it bounds how long stockstays sellable and how far a customer's renewal may extend. Set once at import and nevermoved afterwards - a supplier voucher cannot be extended by us.",
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "fuel_vouchers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                comment: "Where the voucher stands: assigned, used, expired, blocked and the rest.",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<byte[]>(
                name: "qr_payload_sha256",
                table: "fuel_vouchers",
                type: "bytea",
                nullable: false,
                comment: "SHA-256 of the QR payload, carrying the deduplication the ciphertext cannot. Encryptedbytes cannot be compared - AES-GCM produces a fresh nonce every time, so the same QRencrypts differently every time - so the uniqueness index moved here. A hash is notreversible, and it is still a fingerprint of a redeemable code, so it is not somethingto log or expose.",
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AlterColumn<byte[]>(
                name: "qr_payload",
                table: "fuel_vouchers",
                type: "bytea",
                nullable: false,
                comment: "The supplier's QR code, verbatim - the redeemable material. Stored as AES-GCMciphertext: what sits in the column is nonce, tag and ciphertext concatenated.",
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                comment: "The order it was sold with, when it was sold with one.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "liters",
                table: "fuel_vouchers",
                type: "numeric(10,2)",
                nullable: false,
                comment: "How many litres this voucher's paper is for.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "issued_at_utc",
                table: "fuel_vouchers",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When this voucher was handed to a worker by a company owner. Set once, on issue; nevermoved afterwards. A column of its own rather than a reading of updated_at_utc, becauseevery mutation - a block, a recall, a deactivate - clobbers updated_at_utc, and a usagereport that answered 'when did I receive this' off it would move the date every time theowner touched it.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_deleted",
                table: "fuel_vouchers",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Soft delete. The row stays: the paper was real, and the record of it has to survive thedelete.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<Guid>(
                name: "import_job_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                comment: "The import it came from.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "data_kind",
                table: "fuel_vouchers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                comment: "Whether this row is business or a rehearsal: 'real' or 'qa'.",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "customer_expiration_date",
                table: "fuel_vouchers",
                type: "date",
                nullable: false,
                comment: "What the customer was actually sold, and the date their wallet shows. Normally equal tosupplier_expiration_date; it is a separate column because we sell SHORT terms - avoucher with three months of real life may be sold with one week - and only this datemoves when the customer pays to extend it.",
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<decimal>(
                name: "cost_per_liter",
                table: "fuel_vouchers",
                type: "numeric(10,4)",
                nullable: true,
                comment: "What this litre cost US, in UAH - the supplier's price, not our selling price.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: false,
                comment: "The brand's id - the key the text above is a copy of. The composite foreign key(fuel_type_id, brand_id) -> fuel_types(id, brand_id) is what refuses a row whose brandis not its fuel's brand. A nullable key would be a key the database does not check,because a foreign key with a NULL in it is not compared at all.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "brand",
                table: "fuel_vouchers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "The brand's NAME - what a report groups by and what a parser reads. The id below is whatthe database can check, and the two are the same fact written twice.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<Guid>(
                name: "assigned_to_user_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                comment: "The customer who holds it.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "holder_kind",
                table: "fuel_vouchers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                computedColumnSql: "CASE\n    WHEN worker_user_id IS NOT NULL THEN 'Worker'\n    WHEN legal_entity_id IS NOT NULL AND assigned_to_user_id IS NOT NULL THEN 'Company'\n    WHEN assigned_to_user_id IS NOT NULL THEN 'Personal'\n    ELSE 'Stock'\nEND",
                stored: true,
                comment: "Who may redeem it: the customer, or the worker it was issued to.",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldComputedColumnSql: "CASE\n    WHEN worker_user_id IS NOT NULL THEN 'Worker'\n    WHEN legal_entity_id IS NOT NULL AND assigned_to_user_id IS NOT NULL THEN 'Company'\n    WHEN assigned_to_user_id IS NOT NULL THEN 'Personal'\n    ELSE 'Stock'\nEND",
                oldStored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "unit_price",
                table: "order_line_items",
                type: "numeric(12,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldComment: "What the customer paid for ONE unit, in UAH.");

            migrationBuilder.AlterColumn<string>(
                name: "term_code",
                table: "order_line_items",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldNullable: true,
                oldComment: "The term the customer bought this fuel for, frozen at checkout. NULL means the fullremaining supplier term was sold. A renewal line ALWAYS has one.");

            migrationBuilder.AlterColumn<Guid>(
                name: "source_voucher_id",
                table: "order_line_items",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The voucher this line renews, when the line is a renewal; NULL on every other kind ofline. It lives on the LINE and not on the fulfilment: a renewal is an intent - it existsfrom checkout, before payment and before any stock is assigned - and a paid renewal thatfinds no stock has no fulfilment row at all.");

            migrationBuilder.AlterColumn<string>(
                name: "renewal_branch",
                table: "order_line_items",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true,
                oldComment: "Which fulfilment path this renewal took: 'extend' or 'replace'. NULL on every other kindof line. Stored rather than derived, because the branch is DECIDED AND PRICED atcheckout from the voucher's state that day, while fulfilment deliberately re-decides itfrom the current state - the voucher can lapse in between - so nothing reading it backlater can recompute the original answer.");

            migrationBuilder.AlterColumn<int>(
                name: "quantity",
                table: "order_line_items",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1,
                oldComment: "How many units of this line the customer bought.");

            migrationBuilder.AlterColumn<string>(
                name: "pricing_basis",
                table: "order_line_items",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "The inputs that produced unit_price, frozen at checkout, as a JSON object: the per-litrecost, the margin, the pump price, the minimum discount and the terms id this line waspriced under. unit_price records WHAT the customer paid; this records WHY. NULL forevery order placed before the column existed, and deliberately not backfilled:backfilling from today's contract would manufacture a history that never happened, whichis worse than an honest gap - it looks like evidence.");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "previous_customer_expiration",
                table: "order_line_items",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true,
                oldComment: "The customer's expiry on the source voucher BEFORE this renewal was applied. NULL untilfulfilment, and on every non-renewal line. A snapshot, because there is nothing left toread it from afterwards: the extend branch overwritesfuel_vouchers.customer_expiration_date in place, and no history column survives it.");

            migrationBuilder.AlterColumn<decimal>(
                name: "original_line_total",
                table: "order_line_items",
                type: "numeric(12,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true,
                oldComment: "The pump-based LIST total for this line, in UAH, frozen at checkout. The customer'ssaving against the pump is original_line_total - line_total. NULL for rows predating thecolumn (no backfill - a historical pump price cannot be reconstructed) and for lineswith no pump reference (a voucher renewal), which therefore contribute no saving.");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "new_customer_expiration",
                table: "order_line_items",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true,
                oldComment: "The expiry this renewal gave the customer. On a REPLACE this is the PROMISE, not thereplacement voucher's real life: what the customer bought is what they can rely on, andany longer real life on the stock voucher is a hidden reserve they may extend againlater - and showing it here would be selling twice.");

            migrationBuilder.AlterColumn<decimal>(
                name: "liters",
                table: "order_line_items",
                type: "numeric(10,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldComment: "How many litres one unit of this line is.");

            migrationBuilder.AlterColumn<decimal>(
                name: "line_total",
                table: "order_line_items",
                type: "numeric(12,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldComment: "What the customer paid for the WHOLE line, in UAH. This is the truth of the money;original_line_total is the reference it is measured against.");

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "order_line_items",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The brand's id - the key the text above is a copy of. The composite foreign key(fuel_type_id, brand_id) -> fuel_types(id, brand_id) is what refuses a row whose brandis not its fuel's brand. A nullable key would be a key the database does not check,because a foreign key with a NULL in it is not compared at all.");

            migrationBuilder.AlterColumn<string>(
                name: "brand",
                table: "order_line_items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "The brand's NAME - what a report groups by and what a parser reads. The id below is whatthe database can check, and the two are the same fact written twice.");

            migrationBuilder.AlterColumn<Guid>(
                name: "worker_user_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The worker it was issued to.");

            migrationBuilder.AlterColumn<string>(
                name: "voucher_number",
                table: "fuel_vouchers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "The voucher's own number, as the supplier printed it.");

            migrationBuilder.AlterColumn<int>(
                name: "verification_total_modules",
                table: "fuel_vouchers",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "How many modules were checked.");

            migrationBuilder.AlterColumn<int>(
                name: "verification_mismatched_modules",
                table: "fuel_vouchers",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "How many modules disagreed.");

            migrationBuilder.AlterColumn<decimal>(
                name: "verification_mismatch_percent",
                table: "fuel_vouchers",
                type: "numeric(5,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,4)",
                oldNullable: true,
                oldComment: "How far the verification of this voucher's modules disagreed, as a percentage. NULL whenit was never verified.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "used_at_utc",
                table: "fuel_vouchers",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When this voucher was actually refuelled with. Set once, when it is marked used; nevermoved afterwards. NULL while the voucher is still unused.");

            migrationBuilder.AlterColumn<Guid>(
                name: "supplier_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Who we bought it from.");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "supplier_expiration_date",
                table: "fuel_vouchers",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldComment: "The real term of the SUPPLIER's voucher, read verbatim from the printed document atimport. It is the hard ceiling on everything we may promise: it bounds how long stockstays sellable and how far a customer's renewal may extend. Set once at import and nevermoved afterwards - a supplier voucher cannot be extended by us.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "fuel_vouchers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldComment: "Where the voucher stands: assigned, used, expired, blocked and the rest.");

            migrationBuilder.AlterColumn<byte[]>(
                name: "qr_payload_sha256",
                table: "fuel_vouchers",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldComment: "SHA-256 of the QR payload, carrying the deduplication the ciphertext cannot. Encryptedbytes cannot be compared - AES-GCM produces a fresh nonce every time, so the same QRencrypts differently every time - so the uniqueness index moved here. A hash is notreversible, and it is still a fingerprint of a redeemable code, so it is not somethingto log or expose.");

            migrationBuilder.AlterColumn<byte[]>(
                name: "qr_payload",
                table: "fuel_vouchers",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldComment: "The supplier's QR code, verbatim - the redeemable material. Stored as AES-GCMciphertext: what sits in the column is nonce, tag and ciphertext concatenated.");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The order it was sold with, when it was sold with one.");

            migrationBuilder.AlterColumn<decimal>(
                name: "liters",
                table: "fuel_vouchers",
                type: "numeric(10,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldComment: "How many litres this voucher's paper is for.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "issued_at_utc",
                table: "fuel_vouchers",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When this voucher was handed to a worker by a company owner. Set once, on issue; nevermoved afterwards. A column of its own rather than a reading of updated_at_utc, becauseevery mutation - a block, a recall, a deactivate - clobbers updated_at_utc, and a usagereport that answered 'when did I receive this' off it would move the date every time theowner touched it.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_deleted",
                table: "fuel_vouchers",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false,
                oldComment: "Soft delete. The row stays: the paper was real, and the record of it has to survive thedelete.");

            migrationBuilder.AlterColumn<Guid>(
                name: "import_job_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The import it came from.");

            migrationBuilder.AlterColumn<string>(
                name: "data_kind",
                table: "fuel_vouchers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldComment: "Whether this row is business or a rehearsal: 'real' or 'qa'.");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "customer_expiration_date",
                table: "fuel_vouchers",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldComment: "What the customer was actually sold, and the date their wallet shows. Normally equal tosupplier_expiration_date; it is a separate column because we sell SHORT terms - avoucher with three months of real life may be sold with one week - and only this datemoves when the customer pays to extend it.");

            migrationBuilder.AlterColumn<decimal>(
                name: "cost_per_liter",
                table: "fuel_vouchers",
                type: "numeric(10,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "What this litre cost US, in UAH - the supplier's price, not our selling price.");

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The brand's id - the key the text above is a copy of. The composite foreign key(fuel_type_id, brand_id) -> fuel_types(id, brand_id) is what refuses a row whose brandis not its fuel's brand. A nullable key would be a key the database does not check,because a foreign key with a NULL in it is not compared at all.");

            migrationBuilder.AlterColumn<string>(
                name: "brand",
                table: "fuel_vouchers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "The brand's NAME - what a report groups by and what a parser reads. The id below is whatthe database can check, and the two are the same fact written twice.");

            migrationBuilder.AlterColumn<Guid>(
                name: "assigned_to_user_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The customer who holds it.");

            migrationBuilder.AlterColumn<string>(
                name: "holder_kind",
                table: "fuel_vouchers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                computedColumnSql: "CASE\n    WHEN worker_user_id IS NOT NULL THEN 'Worker'\n    WHEN legal_entity_id IS NOT NULL AND assigned_to_user_id IS NOT NULL THEN 'Company'\n    WHEN assigned_to_user_id IS NOT NULL THEN 'Personal'\n    ELSE 'Stock'\nEND",
                stored: true,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldComputedColumnSql: "CASE\n    WHEN worker_user_id IS NOT NULL THEN 'Worker'\n    WHEN legal_entity_id IS NOT NULL AND assigned_to_user_id IS NOT NULL THEN 'Company'\n    WHEN assigned_to_user_id IS NOT NULL THEN 'Personal'\n    ELSE 'Stock'\nEND",
                oldStored: true,
                oldComment: "Who may redeem it: the customer, or the worker it was issued to.");
        }
    }
}
