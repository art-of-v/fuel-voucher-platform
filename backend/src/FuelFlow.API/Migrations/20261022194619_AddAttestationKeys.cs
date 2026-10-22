using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAttestationKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attestation_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, comment: "The Apple App Attest key id, as the client reports it. Unique: an attestation registers exactly one server-side key."),
                    device_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, comment: "The device that registered the key. Attestation is device-bound, not account-bound; a logout revokes the session but does not delete this key."),
                    public_key = table.Column<string>(type: "text", nullable: false, comment: "The public key from the verified attestation, base64. Assertion signatures are checked against this server-owned value, not against anything the client sends."),
                    counter = table.Column<long>(type: "bigint", nullable: false, comment: "The last accepted assertion counter. App Attest counters only move forward, so an assertion at or below this value is a replay."),
                    environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "The App Attest environment the key was attested in: Production or Development."),
                    receipt = table.Column<string>(type: "text", nullable: true, comment: "The attestation receipt, when the verifier supplies one. Audit data only."),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "When the key was registered."),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "When an assertion from this key was last accepted."),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "When an operator revoked the key. A revoked key is never accepted again.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attestation_keys", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attestation_keys_device_id",
                table: "attestation_keys",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_attestation_keys_key_id",
                table: "attestation_keys",
                column: "key_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attestation_keys");
        }
    }
}
