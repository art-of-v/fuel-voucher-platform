using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// D-02 "Consequence - ownership becomes enforceable": the four-row ownership matrix from
    /// MULTI_COMPANY.md §4.3, expressed once, in the database.
    /// <para>
    /// A voucher's three ownership columns are ambiguous by construction. Issuing fuel to a worker
    /// sets worker_user_id and deliberately LEAVES assigned_to_user_id on the owner, so after an
    /// issuance the columns name two different people and "assigned to" is no longer the same
    /// question as "held by". Reading the answer from the right column is the kind of thing every
    /// handler gets right until one of them does not - and one did: GET /vouchers/my matched the owner
    /// while GET /vouchers/{id}/qr matched the worker, so the owner who had just issued fuel could
    /// still spend it (#1186).
    /// </para>
    /// <para>
    /// holder_kind is STORED, not VIRTUAL, for two reasons: ck_voucher_holder reads it, and a stored
    /// value is recomputed on every write rather than only on read, so the answer can never disagree
    /// with the row it describes. Postgres permits a CHECK to reference a generated column; that was
    /// verified against 16 rather than assumed.
    /// </para>
    /// <para>
    /// Safe to apply on a populated table: the only combination the matrix forbids is a worker with
    /// no company, and the sole writer of worker_user_id (GiftVouchersCommand) already refuses a
    /// voucher whose legal_entity_id is not the issuing company. Production held 0 such rows when this
    /// was written.
    /// </para>
    /// </summary>
    public partial class VoucherHolderIsDerived : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "holder_kind",
                table: "fuel_vouchers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                computedColumnSql: "CASE\n    WHEN worker_user_id IS NOT NULL THEN 'Worker'\n    WHEN legal_entity_id IS NOT NULL AND assigned_to_user_id IS NOT NULL THEN 'Company'\n    WHEN assigned_to_user_id IS NOT NULL THEN 'Personal'\n    ELSE 'Stock'\nEND",
                stored: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_holder",
                table: "fuel_vouchers",
                sql: "(holder_kind = 'Stock'    AND assigned_to_user_id IS NULL)\nOR (holder_kind = 'Worker'   AND legal_entity_id IS NOT NULL AND worker_user_id IS NOT NULL)\nOR (holder_kind = 'Personal' AND worker_user_id IS NULL AND assigned_to_user_id IS NOT NULL)\nOR (holder_kind = 'Company'  AND worker_user_id IS NULL AND assigned_to_user_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_holder",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "holder_kind",
                table: "fuel_vouchers");
        }
    }
}
