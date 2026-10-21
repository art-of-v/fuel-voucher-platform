using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheExchangePairsLiveInTheLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The contract step of the fold (planning #319). The pairs live in the ledger now: the
            // row of the retired paper names the replacement, the pair's share of the doplata and the
            // cost stamped on the replacement, and the settlement's own facts live on the batch.
            //
            // The guard below is the point of the step. A drop is irreversible, so the migration
            // refuses to run while a pair exists that the ledger does not carry - which is exactly
            // the state a database is in if an exchange was confirmed between the shadow write
            // (20261021131503) and this deploy. On a database where every pair is in the ledger the
            // check finds nothing and the table goes. On production (2026-10-21) it holds zero rows,
            // so this is a no-op there.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM voucher_exchanges e
                        WHERE NOT EXISTS (
                            SELECT 1
                            FROM voucher_movements m
                            WHERE m.kind = 'exchange_out'
                              AND m.voucher_id = e.old_voucher_id
                              AND m.batch_id = e.exchange_batch_id
                              AND m.replaced_by_voucher_id IS NOT DISTINCT FROM e.new_voucher_id
                              AND m.surcharge_share_uah = e.surcharge_uah
                              AND m.applied_cost_per_liter IS NOT DISTINCT FROM e.cost_per_liter_applied
                        )
                    ) THEN
                        RAISE EXCEPTION 'voucher_exchanges still holds pairs the ledger does not carry: the shadow write has not run for them. Nothing was dropped.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "voucher_exchanges");

            // The folded form of the UNIQUE index the pairs used to carry on their retired voucher:
            // a piece of paper goes to the supplier at most once.
            migrationBuilder.CreateIndex(
                name: "ix_voucher_movements_exchange_out",
                table: "voucher_movements",
                column: "voucher_id",
                unique: true,
                filter: "kind = 'exchange_out'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_voucher_movements_exchange_out",
                table: "voucher_movements");

            migrationBuilder.CreateTable(
                name: "voucher_exchanges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_batch_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Groups every old voucher retired in one confirm action. The batch's lifecycle lives inmovement_batches, which this points at."),
                    new_voucher_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "The new stock voucher this old one was replaced by, or NULL when the old voucher was notpaired - more old than new in its bucket."),
                    old_voucher_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The retired stock voucher."),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The supplier the old vouchers were bought from and the new ones were obtained from. Oneexchange involves exactly one supplier, so every row of a batch carries the same value - butit is recorded per row because this table is also read one voucher at a time during supplierreconciliation, where the row alone has to answer 'who do we settle with for this voucher'."),
                    acting_user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The admin who recorded the exchange."),
                    acting_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "The admin's display name at the time, for the audit. The name is frozen, so it still readsright after the person is renamed or leaves."),
                    brand = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "The brand of the retired voucher (okko/wog/...), denormalized for audit."),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The brand's id - the key the text above is a copy of. The composite foreign key(fuel_type_id, brand_id) -> fuel_types(id, brand_id) is what refuses a row whose brand isnot its fuel's brand."),
                    cost_per_liter_applied = table.Column<decimal>(type: "numeric(10,4)", nullable: true, comment: "The cost per litre set on the new fuel batch for this exchange (the old blended cost plus thesurcharge per litre), when there is one."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "When the exchange was recorded."),
                    fuel_type_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The fuel of the retired voucher, and of its paired replacement - the same bucket."),
                    surcharge_uah = table.Column<decimal>(type: "numeric(10,2)", nullable: false, comment: "This row's OWN share of the batch surcharge - the brand charge spread pro-rata over thispair's litres, NOT the batch total repeated. Do not read one row and call it the batchcharge: a four-voucher exchange would report a quarter of what was actually paid. Sum therows of an ExchangeBatchId."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voucher_exchanges", x => x.id);
                    table.CheckConstraint("ck_voucher_exchange_cost_non_negative", "cost_per_liter_applied IS NULL OR cost_per_liter_applied >= 0");
                    table.CheckConstraint("ck_voucher_exchange_not_self", "old_voucher_id IS DISTINCT FROM new_voucher_id");
                    table.CheckConstraint("ck_voucher_exchange_surcharge_non_negative", "surcharge_uah >= 0");
                    table.ForeignKey(
                        name: "FK_voucher_exchanges_fuel_types_fuel_type_id_brand_id",
                        columns: x => new { x.fuel_type_id, x.brand_id },
                        principalTable: "fuel_types",
                        principalColumns: new[] { "id", "brand_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_voucher_exchanges_fuel_vouchers_new_voucher_id",
                        column: x => x.new_voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_voucher_exchanges_fuel_vouchers_old_voucher_id",
                        column: x => x.old_voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_voucher_exchanges_movement_batches_exchange_batch_id",
                        column: x => x.exchange_batch_id,
                        principalTable: "movement_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_voucher_exchanges_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_voucher_exchanges_exchange_batch_id",
                table: "voucher_exchanges",
                column: "exchange_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_exchanges_fuel_type_id_brand_id",
                table: "voucher_exchanges",
                columns: new[] { "fuel_type_id", "brand_id" });

            migrationBuilder.CreateIndex(
                name: "ix_voucher_exchanges_new_voucher_id",
                table: "voucher_exchanges",
                column: "new_voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_exchanges_old_voucher_id",
                table: "voucher_exchanges",
                column: "old_voucher_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_voucher_exchanges_supplier_id",
                table: "voucher_exchanges",
                column: "supplier_id");
        }
    }
}
