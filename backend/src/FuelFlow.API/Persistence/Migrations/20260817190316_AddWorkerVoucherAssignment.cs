using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerVoucherAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "legal_entity_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "worker_user_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_legal_entity_id",
                table: "fuel_vouchers",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_legal_entity_id_worker_user_id_status",
                table: "fuel_vouchers",
                columns: new[] { "legal_entity_id", "worker_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_worker_user_id",
                table: "fuel_vouchers",
                column: "worker_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_legal_entities_legal_entity_id",
                table: "fuel_vouchers",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_users_worker_user_id",
                table: "fuel_vouchers",
                column: "worker_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fuel_vouchers_legal_entities_legal_entity_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_vouchers_users_worker_user_id",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_legal_entity_id",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_legal_entity_id_worker_user_id_status",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_worker_user_id",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "legal_entity_id",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "worker_user_id",
                table: "fuel_vouchers");
        }
    }
}
