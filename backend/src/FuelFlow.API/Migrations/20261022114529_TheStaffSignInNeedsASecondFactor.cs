using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheStaffSignInNeedsASecondFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_two_factor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The staff account the second factor belongs to. One row per account: a factor is bound to the person, not to the phone they happen to carry."),
                    secret_ciphertext = table.Column<byte[]>(type: "bytea", nullable: true, comment: "The TOTP secret, encrypted with the environment's AES-256 key. NULL while the account has no second factor yet - a secret that was handed out and never proven lives in the pending column instead."),
                    pending_secret_ciphertext = table.Column<byte[]>(type: "bytea", nullable: true, comment: "A secret that was sent to a client and has not been proven yet, encrypted. The next sign-in attempt overwrites it, so an enrolment nobody finished never becomes a factor nobody chose."),
                    pending_challenge_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "The hash of the outstanding challenge - the proof that the first factor was verified for this account and that a second factor may now be attempted. NULL when no challenge is outstanding."),
                    pending_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "When the outstanding challenge stops being redeemable."),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "How many times a code was tried against the outstanding challenge and failed. The challenge is burned once the limit is reached, so one challenge cannot be brute-forced."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "When the row was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "When the row last changed."),
                    last_verified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "When a code was last accepted for this account, or NULL while it never has been.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_two_factor", x => x.id);
                    table.ForeignKey(
                        name: "FK_staff_two_factor_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_staff_two_factor_pending_challenge_hash",
                table: "staff_two_factor",
                column: "pending_challenge_hash",
                filter: "pending_challenge_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_staff_two_factor_user_id",
                table: "staff_two_factor",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff_two_factor");
        }
    }
}
