using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// D-13 / planning issue #319, stage 1 of 4: create the table
    /// <c>voucher_exchanges.exchange_batch_id</c> has pointed at since 2026-10-01 without existing.
    ///
    /// <para>
    /// Additive on purpose. This migration creates the parent and stops; the foreign key lands in the
    /// next one, after a backfill, so that the constraint can never be added against rows that have
    /// no parent yet. Splitting them is also what makes each half independently reversible.
    /// </para>
    ///
    /// <para>
    /// Narrower than TARGET_SCHEMA's D-13 by two columns, both deliberately:
    /// <c>supplier_id</c> points at <c>suppliers</c> rather than at <c>legal_entities(party_id)</c>,
    /// because <c>parties</c> is D-02 and does not exist; and <c>opened_by_user_id</c> carries no
    /// foreign key, because a settlement record that refuses to be written over a missing actor row is
    /// worse than a row carrying an id nobody can resolve - the rule §G states in general terms and
    /// that <c>voucher_exchanges.acting_user_id</c> already follows.
    /// </para>
    ///
    /// <para>
    /// <b>No data migration is needed on production</b> - it holds zero <c>voucher_exchanges</c> rows.
    /// The backfill in the following migration is written to be correct regardless, because staging and
    /// the integration fixtures do hold rows, and a migration that is only correct on an empty table
    /// is not a migration.
    /// </para>
    /// </summary>
    public partial class MovementBatchTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "movement_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    surcharge_uah = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    opened_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    opened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    confirmed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movement_batches", x => x.id);
                    table.CheckConstraint("ck_movement_batches_confirmed_has_timestamp", "(status = 'confirmed') = (confirmed_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_movement_batches_kind", "kind IN ('voucher_exchange','bulk_issuance')");
                    table.CheckConstraint("ck_movement_batches_rejection_reason", "(status = 'rejected') = (rejection_reason IS NOT NULL)");
                    table.CheckConstraint("ck_movement_batches_status", "status IN ('draft','confirmed','rejected','cancelled')");
                    table.CheckConstraint("ck_movement_batches_supplier_matches_kind", "kind <> 'voucher_exchange' OR supplier_id IS NOT NULL");
                    table.CheckConstraint("ck_movement_batches_surcharge_non_negative", "surcharge_uah >= 0");
                    table.ForeignKey(
                        name: "FK_movement_batches_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_movement_batches_opened_at_utc",
                table: "movement_batches",
                column: "opened_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_movement_batches_supplier_id",
                table: "movement_batches",
                column: "supplier_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movement_batches");
        }
    }
}
