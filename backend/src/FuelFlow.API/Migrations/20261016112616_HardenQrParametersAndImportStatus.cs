using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class HardenQrParametersAndImportStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------------------------------------------------------------------------------
            // Pre-flight. Every statement below can fail on data that already violates it, and a
            // non-zero count is a FINDING rather than something to work around:
            //
            //   SELECT ecc_level, version, mask_pattern, encoding_mode, count(*)
            //     FROM qr_parameters
            //    GROUP BY 1,2,3,4 HAVING count(*) > 1;          -- the duplicates this dedupes
            //
            //   SELECT count(*) FROM qr_parameters
            //    WHERE ecc_level NOT IN ('L','M','Q','H')
            //       OR (version IS NOT NULL AND version NOT BETWEEN 1 AND 40)
            //       OR (mask_pattern IS NOT NULL AND mask_pattern NOT BETWEEN 0 AND 7);
            //
            //   SELECT DISTINCT status FROM voucher_imports WHERE status NOT IN ('Started','Completed','Failed');
            //   SELECT count(*) FROM voucher_imports
            //    WHERE (status = 'Started') <> (completed_at_utc IS NULL);
            //
            // Both tables are tiny - qr_parameters holds about a dozen rows, voucher_imports one per
            // PDF - so the constraints are added directly rather than through NOT VALID + VALIDATE.
            // The ceremony is for tables where the scan blocks writes, and these do not.
            // ---------------------------------------------------------------------------------

            // Re-point before deleting. fuel_vouchers.qr_parameters_id is SET NULL on delete, so
            // dropping a duplicate outright would silently unlink every voucher that used it - the
            // QR would still render from its own payload, but the configuration that produced it
            // would be gone and the row would claim to have none. Keeping the oldest of each group
            // and moving the references costs one statement each and loses nothing.
            //
            // PARTITION BY groups NULLs together, which is the same rule NULLS NOT DISTINCT applies,
            // so the rows this collapses are exactly the rows the new index would reject.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT id,
                           first_value(id) OVER (
                               PARTITION BY ecc_level, version, mask_pattern, encoding_mode
                               ORDER BY created_at_utc, id
                           ) AS keeper_id
                      FROM qr_parameters
                )
                UPDATE fuel_vouchers v
                   SET qr_parameters_id = r.keeper_id
                  FROM ranked r
                 WHERE v.qr_parameters_id = r.id
                   AND r.id <> r.keeper_id;
                """);

            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT id,
                           row_number() OVER (
                               PARTITION BY ecc_level, version, mask_pattern, encoding_mode
                               ORDER BY created_at_utc, id
                           ) AS rn
                      FROM qr_parameters
                )
                DELETE FROM qr_parameters q
                 USING ranked r
                 WHERE q.id = r.id
                   AND r.rn > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_qr_parameters_ecc_level_version_mask_pattern_encoding_mode",
                table: "qr_parameters");

            migrationBuilder.AddCheckConstraint(
                name: "ck_qr_parameters_ecc_level",
                table: "qr_parameters",
                sql: "ecc_level IN ('L','M','Q','H')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_qr_parameters_version",
                table: "qr_parameters",
                sql: "version IS NULL OR version BETWEEN 1 AND 40");

            migrationBuilder.AddCheckConstraint(
                name: "ck_qr_parameters_mask_pattern",
                table: "qr_parameters",
                sql: "mask_pattern IS NULL OR mask_pattern BETWEEN 0 AND 7");

            // The point of the whole migration. Without NULLS NOT DISTINCT this index cannot reject
            // ('M', NULL, NULL, NULL), which is the exact shape F-05's leak produced - one junk row
            // per import whose PDF had an unreadable ECC level.
            //
            // `.AreNullsDistinct(false)` on the model does reach the generated SQL as this
            // annotation; verified against PG16, where `pg_index.indnullsnotdistinct` is true for an
            // index created this way and false for an ordinary unique btree. QrParametersAndImportStatusIntegrityTests
            // asserts both halves - that the flag is set, and that a row of all NULLs is refused -
            // so a later migration that recreates the index the plain way fails rather than silently
            // restoring the defect.
            migrationBuilder.CreateIndex(
                name: "IX_qr_parameters_ecc_level_version_mask_pattern_encoding_mode",
                table: "qr_parameters",
                columns: new[] { "ecc_level", "version", "mask_pattern", "encoding_mode" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_imports_status",
                table: "voucher_imports",
                sql: "status IN ('Started','Completed','Failed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_imports_completed",
                table: "voucher_imports",
                sql: "(status = 'Started') = (completed_at_utc IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_imports_completed",
                table: "voucher_imports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_imports_status",
                table: "voucher_imports");

            migrationBuilder.DropIndex(
                name: "IX_qr_parameters_ecc_level_version_mask_pattern_encoding_mode",
                table: "qr_parameters");

            migrationBuilder.DropCheckConstraint(
                name: "ck_qr_parameters_ecc_level",
                table: "qr_parameters");

            migrationBuilder.DropCheckConstraint(
                name: "ck_qr_parameters_mask_pattern",
                table: "qr_parameters");

            migrationBuilder.DropCheckConstraint(
                name: "ck_qr_parameters_version",
                table: "qr_parameters");

            migrationBuilder.CreateIndex(
                name: "IX_qr_parameters_ecc_level_version_mask_pattern_encoding_mode",
                table: "qr_parameters",
                columns: new[] { "ecc_level", "version", "mask_pattern", "encoding_mode" },
                unique: true);
        }
    }
}
