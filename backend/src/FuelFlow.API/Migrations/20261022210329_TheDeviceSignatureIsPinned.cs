using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheDeviceSignatureIsPinned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "signature_algorithm",
                table: "devices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "The only signature scheme this device may use. NULL = legacy row, pinned to the scheme that first verifies.");

            migrationBuilder.AddColumn<int>(
                name: "signature_payload_version",
                table: "devices",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "The shape of the signed bytes: 1 = raw legacy payload, 2 = domain-separated FuelFlow-DeviceSignature-v2.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_devices_signature_algorithm",
                table: "devices",
                sql: "signature_algorithm IS NULL OR signature_algorithm IN ('RsaPkcs1Sha256', 'RsaPssSha256', 'EcdsaSha256Der', 'EcdsaSha256P1363')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_devices_signature_payload_version",
                table: "devices",
                sql: "signature_payload_version IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_devices_signature_algorithm",
                table: "devices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_devices_signature_payload_version",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "signature_algorithm",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "signature_payload_version",
                table: "devices");
        }
    }
}
