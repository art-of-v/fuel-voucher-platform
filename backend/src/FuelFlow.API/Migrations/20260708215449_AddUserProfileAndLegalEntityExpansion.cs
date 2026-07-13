using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfileAndLegalEntityExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "birthdate",
                table: "users",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_name",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "profile_image_url",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signature_data",
                table: "user_contracts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "legal_entities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at_utc",
                table: "legal_entities",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "director_name",
                table: "legal_entities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "edrpou",
                table: "legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "legal_entities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at_utc",
                table: "legal_entities",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "legal_entities",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "vat_number",
                table: "legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_legal_entities_edrpou",
                table: "legal_entities",
                column: "edrpou",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legal_entities_user_id",
                table: "legal_entities",
                column: "user_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_legal_entities_users_user_id",
                table: "legal_entities",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_legal_entities_users_user_id",
                table: "legal_entities");

            migrationBuilder.DropIndex(
                name: "IX_legal_entities_edrpou",
                table: "legal_entities");

            migrationBuilder.DropIndex(
                name: "IX_legal_entities_user_id",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "birthdate",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email",
                table: "users");

            migrationBuilder.DropColumn(
                name: "first_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "profile_image_url",
                table: "users");

            migrationBuilder.DropColumn(
                name: "signature_data",
                table: "user_contracts");

            migrationBuilder.DropColumn(
                name: "address",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "created_at_utc",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "director_name",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "edrpou",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "email",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "vat_number",
                table: "legal_entities");
        }
    }
}
