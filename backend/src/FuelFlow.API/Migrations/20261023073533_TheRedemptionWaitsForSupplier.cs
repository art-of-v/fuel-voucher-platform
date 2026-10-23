using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheRedemptionWaitsForSupplier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_statuses",
                table: "voucher_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_held_has_order",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_status",
                table: "fuel_vouchers");

            migrationBuilder.CreateTable(
                name: "voucher_redemptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The voucher this claim is about."),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Where the claim stands: reported, confirmed, withdrawn, rejected or reversed."),
                    reported_liters = table.Column<decimal>(type: "numeric(10,2)", nullable: false, comment: "The litres claimed by the reporter or statement. Normally the voucher's own litres."),
                    redeemed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "When the fuel was actually taken, as far as the evidence says."),
                    reported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "When this claim entered the system."),
                    reported_by_user_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "The customer who self-reported it. NULL means a supplier statement raised it."),
                    supplier_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "The supplier's line reference, when the claim came from a statement."),
                    confirmed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "When the evidence was accepted."),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "The operator who accepted the evidence. Bare uuid, no foreign key."),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "When the claim was withdrawn, rejected or reversed."),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "The actor who closed the claim. Bare uuid, no foreign key."),
                    resolution_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Why the operator closed it, in their own words."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voucher_redemptions", x => x.id);
                    table.CheckConstraint("ck_voucher_redemptions_closed_shape", "status NOT IN ('Withdrawn', 'Rejected', 'Reversed') OR (closed_at_utc IS NOT NULL AND closed_by_user_id IS NOT NULL)");
                    table.CheckConstraint("ck_voucher_redemptions_confirmed_shape", "status <> 'Confirmed' OR (confirmed_at_utc IS NOT NULL AND confirmed_by_user_id IS NOT NULL)");
                    table.CheckConstraint("ck_voucher_redemptions_liters_positive", "reported_liters > 0");
                    table.CheckConstraint("ck_voucher_redemptions_status", "status IN ('Reported', 'Confirmed', 'Withdrawn', 'Rejected', 'Reversed')");
                    table.ForeignKey(
                        name: "FK_voucher_redemptions_fuel_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "redemption_statement_rows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "The supplier's stable line id. Unique, so a re-import is idempotent."),
                    voucher_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "The voucher number as the statement printed it."),
                    liters = table.Column<decimal>(type: "numeric(10,2)", nullable: false, comment: "The litres the statement attributes to this line."),
                    redeemed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "When the supplier says the fuel was taken, if the statement carries a time."),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "The voucher this line matched. NULL when the number is unknown."),
                    voucher_redemption_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "The claim this line matched. NULL when no active claim existed."),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Where this line stands: matched, confirmed, mismatch, unknown and the rest."),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "The line as the supplier sent it, kept for the operator."),
                    imported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "When this line was imported."),
                    imported_by_user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The operator who imported the statement. Bare uuid, no foreign key."),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "When the line was closed."),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "The operator who closed the line. Bare uuid, no foreign key."),
                    resolution_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Why the operator closed the line.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_redemption_statement_rows", x => x.id);
                    table.CheckConstraint("ck_redemption_statement_rows_liters_positive", "liters > 0");
                    table.CheckConstraint("ck_redemption_statement_rows_matched_has_voucher", "status <> 'Matched' OR voucher_id IS NOT NULL");
                    table.CheckConstraint("ck_redemption_statement_rows_resolved_shape", "status NOT IN ('Confirmed', 'Rejected', 'Reversed', 'Dismissed') OR (resolved_at_utc IS NOT NULL AND resolved_by_user_id IS NOT NULL)");
                    table.CheckConstraint("ck_redemption_statement_rows_status", "status IN ('Matched', 'Confirmed', 'Rejected', 'Reversed', 'UnknownVoucher',\n           'Mismatch', 'AlreadyConfirmed', 'NotAssigned', 'Dismissed')");
                    table.ForeignKey(
                        name: "FK_redemption_statement_rows_fuel_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_redemption_statement_rows_voucher_redemptions_voucher_redem~",
                        column: x => x.voucher_redemption_id,
                        principalTable: "voucher_redemptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_statuses",
                table: "voucher_events",
                sql: "(from_status IS NULL OR from_status IN ('Imported','DecodeIntegrityWarning','DecodeIntegrityFailed','Available','Assigned','PendingReconciliation','Used','Expired','Blocked','Deactivated')) AND (to_status IS NULL OR to_status IN ('Imported','DecodeIntegrityWarning','DecodeIntegrityFailed','Available','Assigned','PendingReconciliation','Used','Expired','Blocked','Deactivated'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_held_has_order",
                table: "fuel_vouchers",
                sql: "status NOT IN ('Assigned', 'PendingReconciliation', 'Used', 'Blocked') OR order_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_status",
                table: "fuel_vouchers",
                sql: "status IN ('Imported', 'DecodeIntegrityWarning', 'DecodeIntegrityFailed', 'Available',\n           'Assigned', 'PendingReconciliation', 'Used', 'Expired', 'Blocked', 'Deactivated')");

            migrationBuilder.CreateIndex(
                name: "ix_redemption_statement_rows_status_imported_at_utc",
                table: "redemption_statement_rows",
                columns: new[] { "status", "imported_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_redemption_statement_rows_voucher_id",
                table: "redemption_statement_rows",
                column: "voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_redemption_statement_rows_voucher_number",
                table: "redemption_statement_rows",
                column: "voucher_number");

            migrationBuilder.CreateIndex(
                name: "ix_redemption_statement_rows_voucher_redemption_id",
                table: "redemption_statement_rows",
                column: "voucher_redemption_id");

            migrationBuilder.CreateIndex(
                name: "ux_redemption_statement_rows_external_id",
                table: "redemption_statement_rows",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_voucher_redemptions_reported_by_user_id_status",
                table: "voucher_redemptions",
                columns: new[] { "reported_by_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_voucher_redemptions_status_reported_at_utc",
                table: "voucher_redemptions",
                columns: new[] { "status", "reported_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_voucher_redemptions_active_voucher",
                table: "voucher_redemptions",
                column: "voucher_id",
                unique: true,
                filter: "status IN ('Reported', 'Confirmed')");

            migrationBuilder.CreateIndex(
                name: "ux_voucher_redemptions_supplier_reference",
                table: "voucher_redemptions",
                column: "supplier_reference",
                unique: true,
                filter: "supplier_reference IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "redemption_statement_rows");

            migrationBuilder.DropTable(
                name: "voucher_redemptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_statuses",
                table: "voucher_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_held_has_order",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_status",
                table: "fuel_vouchers");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_statuses",
                table: "voucher_events",
                sql: "(from_status IS NULL OR from_status IN ('Imported','DecodeIntegrityWarning','DecodeIntegrityFailed','Available','Assigned','Used','Expired','Blocked','Deactivated')) AND (to_status IS NULL OR to_status IN ('Imported','DecodeIntegrityWarning','DecodeIntegrityFailed','Available','Assigned','Used','Expired','Blocked','Deactivated'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_held_has_order",
                table: "fuel_vouchers",
                sql: "status NOT IN ('Assigned', 'Used', 'Blocked') OR order_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_status",
                table: "fuel_vouchers",
                sql: "status IN ('Imported', 'DecodeIntegrityWarning', 'DecodeIntegrityFailed', 'Available',\n           'Assigned', 'Used', 'Expired', 'Blocked', 'Deactivated')");
        }
    }
}
