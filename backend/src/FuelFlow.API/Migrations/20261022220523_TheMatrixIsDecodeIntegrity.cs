using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Separates the QR matrix check from supplier authenticity: the columns and statuses say
    /// "decode integrity" and "matrix mismatch" instead of "verification", which is what they have
    /// always measured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check re-encodes the decoded payload and compares the module grid with the printed image.
    /// A match proves the image and the payload agree; it does not prove the supplier issued the
    /// voucher. No supplier signature, serial or registry check exists in the payload, so the import
    /// now records the declared supplier on the batch for supplier/batch reconciliation and reports
    /// that provenance as unverified.
    /// </para>
    /// <para>
    /// The columns are renamed rather than dropped and re-added, so the recorded mismatch values
    /// survive. The status values in <c>fuel_vouchers</c> and both sides of the audit trail
    /// (<c>voucher_events.from_status</c> / <c>to_status</c>) are rewritten before the CHECKs return.
    /// </para>
    /// </remarks>
    public partial class TheMatrixIsDecodeIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_statuses",
                table: "voucher_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_status",
                table: "fuel_vouchers");

            migrationBuilder.Sql("""
                UPDATE fuel_vouchers
                   SET status = 'DecodeIntegrityWarning'
                 WHERE status = 'VerifiedWithWarnings';

                UPDATE fuel_vouchers
                   SET status = 'DecodeIntegrityFailed'
                 WHERE status = 'VerificationFailed';

                UPDATE voucher_events
                   SET from_status = 'DecodeIntegrityWarning'
                 WHERE from_status = 'VerifiedWithWarnings';

                UPDATE voucher_events
                   SET from_status = 'DecodeIntegrityFailed'
                 WHERE from_status = 'VerificationFailed';

                UPDATE voucher_events
                   SET to_status = 'DecodeIntegrityWarning'
                 WHERE to_status = 'VerifiedWithWarnings';

                UPDATE voucher_events
                   SET to_status = 'DecodeIntegrityFailed'
                 WHERE to_status = 'VerificationFailed';
                """);

            migrationBuilder.RenameColumn(
                name: "verification_mismatch_percent",
                table: "fuel_vouchers",
                newName: "matrix_mismatch_percent");

            migrationBuilder.RenameColumn(
                name: "verification_mismatched_modules",
                table: "fuel_vouchers",
                newName: "matrix_mismatched_modules");

            migrationBuilder.RenameColumn(
                name: "verification_total_modules",
                table: "fuel_vouchers",
                newName: "matrix_total_modules");

            migrationBuilder.RenameColumn(
                name: "verification_failed_count",
                table: "voucher_imports",
                newName: "decode_integrity_failed_count");

            migrationBuilder.RenameColumn(
                name: "verified_with_warnings_count",
                table: "voucher_imports",
                newName: "decode_integrity_warning_count");

            migrationBuilder.Sql("""
                COMMENT ON COLUMN fuel_vouchers.matrix_mismatch_percent IS
                    $c$How far the printed QR matrix disagreed with the decoded payload, as a percentage. NULL when the matrix could not be checked. Not a measure of authenticity.$c$;

                COMMENT ON COLUMN fuel_vouchers.matrix_mismatched_modules IS
                    $c$How many QR modules disagreed between the printed matrix and the regenerated one.$c$;

                COMMENT ON COLUMN fuel_vouchers.matrix_total_modules IS
                    $c$How many QR modules were compared.$c$;

                COMMENT ON COLUMN voucher_imports.decode_integrity_failed_count IS
                    $c$How many rows were read but failed the decode-integrity check: the printed QR matrix disagreed with the decoded payload by more than 2% of modules. Import QA, not a verdict on the supplier.$c$;

                COMMENT ON COLUMN voucher_imports.decode_integrity_warning_count IS
                    $c$How many rows passed the decode-integrity check with a 1-2% module mismatch - something odd in the image, not proof that the voucher is counterfeit or genuine.$c$;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "supplier_id",
                table: "voucher_imports",
                type: "uuid",
                nullable: true,
                comment: "The supplier the operator declared for this delivery. An operator assertion, not a cryptographic or registry verification - no such check exists in the payload.");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_imports_supplier_id",
                table: "voucher_imports",
                column: "supplier_id");

            // Existing imports predate the column. They still have the supplier on every voucher
            // (and, once costed, on the purchase batch); copy it up so supplier/batch
            // reconciliation does not start with a hole for historical stock.
            migrationBuilder.Sql("""
                UPDATE voucher_imports AS vi
                   SET supplier_id = COALESCE(
                       (SELECT MIN(v.supplier_id::text)::uuid
                          FROM fuel_vouchers AS v
                         WHERE v.import_job_id = vi.id
                           AND v.supplier_id IS NOT NULL),
                       (SELECT MIN(pb.supplier_id::text)::uuid
                          FROM purchase_batches AS pb
                         WHERE pb.import_job_id = vi.id))
                 WHERE vi.supplier_id IS NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_statuses",
                table: "voucher_events",
                sql: "(from_status IS NULL OR from_status IN ('Imported','DecodeIntegrityWarning','DecodeIntegrityFailed','Available','Assigned','Used','Expired','Blocked','Deactivated')) AND (to_status IS NULL OR to_status IN ('Imported','DecodeIntegrityWarning','DecodeIntegrityFailed','Available','Assigned','Used','Expired','Blocked','Deactivated'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_status",
                table: "fuel_vouchers",
                sql: "status IN ('Imported', 'DecodeIntegrityWarning', 'DecodeIntegrityFailed', 'Available',\n           'Assigned', 'Used', 'Expired', 'Blocked', 'Deactivated')");

            migrationBuilder.AddForeignKey(
                name: "FK_voucher_imports_suppliers_supplier_id",
                table: "voucher_imports",
                column: "supplier_id",
                principalTable: "suppliers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_voucher_imports_suppliers_supplier_id",
                table: "voucher_imports");

            migrationBuilder.DropIndex(
                name: "ix_voucher_imports_supplier_id",
                table: "voucher_imports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_statuses",
                table: "voucher_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_status",
                table: "fuel_vouchers");

            migrationBuilder.Sql("""
                UPDATE fuel_vouchers
                   SET status = 'VerifiedWithWarnings'
                 WHERE status = 'DecodeIntegrityWarning';

                UPDATE fuel_vouchers
                   SET status = 'VerificationFailed'
                 WHERE status = 'DecodeIntegrityFailed';

                UPDATE voucher_events
                   SET from_status = 'VerifiedWithWarnings'
                 WHERE from_status = 'DecodeIntegrityWarning';

                UPDATE voucher_events
                   SET from_status = 'VerificationFailed'
                 WHERE from_status = 'DecodeIntegrityFailed';

                UPDATE voucher_events
                   SET to_status = 'VerifiedWithWarnings'
                 WHERE to_status = 'DecodeIntegrityWarning';

                UPDATE voucher_events
                   SET to_status = 'VerificationFailed'
                 WHERE to_status = 'DecodeIntegrityFailed';
                """);

            migrationBuilder.DropColumn(
                name: "supplier_id",
                table: "voucher_imports");

            migrationBuilder.RenameColumn(
                name: "matrix_mismatch_percent",
                table: "fuel_vouchers",
                newName: "verification_mismatch_percent");

            migrationBuilder.RenameColumn(
                name: "matrix_mismatched_modules",
                table: "fuel_vouchers",
                newName: "verification_mismatched_modules");

            migrationBuilder.RenameColumn(
                name: "matrix_total_modules",
                table: "fuel_vouchers",
                newName: "verification_total_modules");

            migrationBuilder.RenameColumn(
                name: "decode_integrity_failed_count",
                table: "voucher_imports",
                newName: "verification_failed_count");

            migrationBuilder.RenameColumn(
                name: "decode_integrity_warning_count",
                table: "voucher_imports",
                newName: "verified_with_warnings_count");

            migrationBuilder.Sql("""
                COMMENT ON COLUMN fuel_vouchers.verification_mismatch_percent IS
                    $c$How far the verification of this voucher's modules disagreed, as a percentage. NULL when it was never verified.$c$;

                COMMENT ON COLUMN fuel_vouchers.verification_mismatched_modules IS
                    $c$How many modules disagreed.$c$;

                COMMENT ON COLUMN fuel_vouchers.verification_total_modules IS
                    $c$How many modules were checked.$c$;

                COMMENT ON COLUMN voucher_imports.verification_failed_count IS
                    $c$How many rows were read but did not verify - the code does not match the fuel, the litres do not match the code.$c$;

                COMMENT ON COLUMN voucher_imports.verified_with_warnings_count IS
                    $c$How many rows verified but with a warning - read and checked, with something odd that did not stop them.$c$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_statuses",
                table: "voucher_events",
                sql: "(from_status IS NULL OR from_status IN ('Imported','VerifiedWithWarnings','VerificationFailed','Available','Assigned','Used','Expired','Blocked','Deactivated')) AND (to_status IS NULL OR to_status IN ('Imported','VerifiedWithWarnings','VerificationFailed','Available','Assigned','Used','Expired','Blocked','Deactivated'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_status",
                table: "fuel_vouchers",
                sql: "status IN ('Imported', 'VerifiedWithWarnings', 'VerificationFailed', 'Available',\n           'Assigned', 'Used', 'Expired', 'Blocked', 'Deactivated')");
        }
    }
}
