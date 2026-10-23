using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheOtpHashCarriesAPepper : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "verification_codes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "The code itself, hashed with the server-side OTP pepper (HMAC-SHA256).",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "The code itself, hashed.");

            // Rows written before the pepper existed can never verify again: their bare SHA-256 is
            // exactly the digest #682 removed, and the handler refuses a digest without the v2:
            // marker. Burn them now instead of leaving live-looking rows that can only fail, and so
            // no unpeppered digest is still sitting in the table after the deploy.
            migrationBuilder.Sql(
                "UPDATE verification_codes "
                + "SET is_used = TRUE, used_at_utc = COALESCE(used_at_utc, now()) "
                + "WHERE is_used = FALSE AND code NOT LIKE 'v2:%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately irreversible in data terms: a code burned by Up is gone by design, and
            // restoring it would re-open a pre-pepper digest. Only the comment rolls back.
            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "verification_codes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "The code itself, hashed.",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "The code itself, hashed with the server-side OTP pepper (HMAC-SHA256).");
        }
    }
}
