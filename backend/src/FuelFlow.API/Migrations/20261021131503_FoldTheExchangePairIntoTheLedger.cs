using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class FoldTheExchangePairIntoTheLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "applied_cost_per_liter",
                table: "voucher_movements",
                type: "numeric(12,4)",
                nullable: true,
                comment: "The cost per litre stamped on the replacement when it arrived. A level, not a delta. Recorded AT THE TIME because it cannot be derived afterwards: fuel_vouchers.cost_per_liter is mutable and gets restamped.");

            migrationBuilder.AddColumn<Guid>(
                name: "replaced_by_voucher_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: true,
                comment: "The paper that took this voucher's place, when this paper was traded for another one. NULL for a movement that was not a trade - a sale, a return, an import. The row of the retired paper carries it, not the row of the successor.");

            migrationBuilder.AddColumn<decimal>(
                name: "surcharge_share_uah",
                table: "voucher_movements",
                type: "numeric(12,2)",
                nullable: true,
                comment: "This movement's share of the money a settlement moved: the doplata the operator paid the supplier for this pair of paper. NOT the settlement total - that lives on movement_batches.surcharge_uah, and the shares can sum to less than it.");

            migrationBuilder.AlterColumn<decimal>(
                name: "min_discount_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "The smallest discount we give the customer per litre, so that our price always sits below the column price - we are never dearer than the pump. RETIRED: the price is computed as min(cost + margin, pump), and this column no longer takes part in it. It stays until the migration that drops it.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "The smallest discount we give the customer per litre, so that our price always sits below the column price - we are never dearer than the pump.");

            migrationBuilder.AlterColumn<decimal>(
                name: "base_price",
                table: "fuel_types",
                type: "numeric(12,2)",
                nullable: true,
                comment: "The list price of the fuel per litre - the price before our discount. It is the reference the customer's saving is measured against. RETIRED: the reference is the COLUMN price next door, and the saving is computed from it. The column stays until the migration that drops it.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldComment: "The list price of the fuel per litre - the price before our discount. It is the reference the customer's saving is measured against.");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_movements_replaced_by",
                table: "voucher_movements",
                column: "replaced_by_voucher_id",
                filter: "replaced_by_voucher_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_applied_cost_non_negative",
                table: "voucher_movements",
                sql: "applied_cost_per_liter IS NULL OR applied_cost_per_liter >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_not_self_replaced",
                table: "voucher_movements",
                sql: "replaced_by_voucher_id IS NULL OR replaced_by_voucher_id <> voucher_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_surcharge_share_non_negative",
                table: "voucher_movements",
                sql: "surcharge_share_uah IS NULL OR surcharge_share_uah >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_voucher_movements_fuel_vouchers_replaced_by_voucher_id",
                table: "voucher_movements",
                column: "replaced_by_voucher_id",
                principalTable: "fuel_vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_voucher_movements_fuel_vouchers_replaced_by_voucher_id",
                table: "voucher_movements");

            migrationBuilder.DropIndex(
                name: "ix_voucher_movements_replaced_by",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_applied_cost_non_negative",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_not_self_replaced",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_surcharge_share_non_negative",
                table: "voucher_movements");

            migrationBuilder.DropColumn(
                name: "applied_cost_per_liter",
                table: "voucher_movements");

            migrationBuilder.DropColumn(
                name: "replaced_by_voucher_id",
                table: "voucher_movements");

            migrationBuilder.DropColumn(
                name: "surcharge_share_uah",
                table: "voucher_movements");

            migrationBuilder.AlterColumn<decimal>(
                name: "min_discount_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                comment: "The smallest discount we give the customer per litre, so that our price always sits below the column price - we are never dearer than the pump.",
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true,
                oldComment: "The smallest discount we give the customer per litre, so that our price always sits below the column price - we are never dearer than the pump. RETIRED: the price is computed as min(cost + margin, pump), and this column no longer takes part in it. It stays until the migration that drops it.");

            migrationBuilder.AlterColumn<decimal>(
                name: "base_price",
                table: "fuel_types",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m,
                comment: "The list price of the fuel per litre - the price before our discount. It is the reference the customer's saving is measured against.",
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true,
                oldComment: "The list price of the fuel per litre - the price before our discount. It is the reference the customer's saving is measured against. RETIRED: the reference is the COLUMN price next door, and the saving is computed from it. The column stays until the migration that drops it.");
        }
    }
}
