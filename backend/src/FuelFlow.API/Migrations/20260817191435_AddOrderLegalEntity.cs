using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderLegalEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "legal_entity_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_legal_entity_id",
                table: "orders",
                column: "legal_entity_id");

            migrationBuilder.AddForeignKey(
                name: "FK_orders_legal_entities_legal_entity_id",
                table: "orders",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_legal_entities_legal_entity_id",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_legal_entity_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "legal_entity_id",
                table: "orders");
        }
    }
}
