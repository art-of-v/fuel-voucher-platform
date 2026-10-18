using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class PaymentsMayArriveBeforeAnOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_refund_audit_only_on_refund",
                table: "payments");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_unallocated",
                table: "payments",
                column: "created_at_utc",
                filter: "order_id IS NULL AND status = 'Succeeded'");

            migrationBuilder.CreateIndex(
                name: "IX_payments_user_id",
                table: "payments",
                column: "user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_refund_audit_only_on_refund",
                table: "payments",
                sql: "kind = 'Refund' OR provider <> 'monobank' OR (created_by_user_id IS NULL AND created_by_user_name IS NULL AND NOT is_automatic AND error_message IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_payments_users_user_id",
                table: "payments",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down() cannot restore NOT NULL by guessing. The only value that would fill an orderless
            // payment is the zero GUID, which does not name an order - it produces a row that passes
            // the constraint and points at nothing, which is worse than the constraint failing.
            //
            // So it refuses, by name, and says what to do. A loud failure here is the correct behaviour:
            // silently inventing an order id would destroy the distinction this migration exists to
            // create, and it would do so on the way down, where nobody is watching.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM payments WHERE order_id IS NULL) THEN
                        RAISE EXCEPTION
                            'cannot roll back: % payment(s) exist with no order. Attach or delete them first - this migration will not invent an order id for money we are holding.',
                            (SELECT count(*) FROM payments WHERE order_id IS NULL);
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_payments_users_user_id",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_unallocated",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_user_id",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_refund_audit_only_on_refund",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "payments");

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                table: "payments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_refund_audit_only_on_refund",
                table: "payments",
                sql: "kind = 'Refund' OR (created_by_user_id IS NULL AND created_by_user_name IS NULL AND NOT is_automatic AND error_message IS NULL)");
        }
    }
}
