using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddQaTestAccount : Migration
    {
        // Adds the is_qa_account flag and seeds the single QA App-Store-review test account plus the
        // (off) QA test-access switch. Seeds are written as guarded, idempotent SQL so the migration
        // is safe to re-run and safe where the row was created out-of-band. The QA account carries
        // the least-privilege built-in "User" role and is_active=true so a reviewer can exercise the
        // full workflow (including a test purchase); it can still only authenticate while an admin
        // has QA access enabled AND a QA code is configured, so seeding it grants nothing on its own.
        private const string QaUserId = "a10c1de5-4a11-4b00-8000-000000000001"; // == AuthOptions.QaTestAccountUserId
        private const string QaPhoneNumber = "+380110010203";                   // == AuthOptions.QaTestAccountPhoneNumber
        private const string UserRoleId = "1b445bd0-6f91-4b5a-ac2d-a9601691142f"; // == SeedRoles.UserRoleId
        private const string QaEnabledKey = "QaTestAccess:Enabled";              // == AppSettingKeys.QaTestAccessEnabled

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IF NOT EXISTS: the model was applied to some environments before this migration existed.
            migrationBuilder.Sql(
                """ALTER TABLE users ADD COLUMN IF NOT EXISTS is_qa_account boolean NOT NULL DEFAULT false;""");

            // Seed the QA account. Guarded on the phone number (its unique-when-not-deleted index),
            // so if a row with this number already exists we leave it untouched rather than clashing.
            migrationBuilder.Sql(
                $"""
                INSERT INTO users (
                    id, phone_number, role_id, created_at_utc, updated_at_utc,
                    bonus_balance, is_active, token_version, is_deleted, is_banned, is_qa_account)
                SELECT
                    '{QaUserId}'::uuid, '{QaPhoneNumber}', '{UserRoleId}'::uuid, now(), now(),
                    0, true, 1, false, false, true
                WHERE NOT EXISTS (SELECT 1 FROM users WHERE phone_number = '{QaPhoneNumber}');
                """);

            // Make sure an already-present row is flagged as the QA account (e.g. the number was
            // registered as an ordinary user before this feature). Never touches any other row.
            migrationBuilder.Sql(
                $"""UPDATE users SET is_qa_account = true WHERE phone_number = '{QaPhoneNumber}' AND is_qa_account = false;""");

            // Seed the switch explicitly OFF so the admin panel has a row to show. Absence already
            // means "disabled" (fail-safe), so this is only for a clean initial UI state.
            migrationBuilder.Sql(
                $"""
                INSERT INTO app_settings (key, value, updated_at_utc)
                SELECT '{QaEnabledKey}', 'false', now()
                WHERE NOT EXISTS (SELECT 1 FROM app_settings WHERE key = '{QaEnabledKey}');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the seeded switch and QA account, then drop the column. Only the seeded QA row
            // is deleted (matched by its fixed id), never a real customer.
            migrationBuilder.Sql($"""DELETE FROM app_settings WHERE key = '{QaEnabledKey}';""");
            migrationBuilder.Sql($"""DELETE FROM users WHERE id = '{QaUserId}'::uuid;""");
            migrationBuilder.Sql("""ALTER TABLE users DROP COLUMN IF EXISTS is_qa_account;""");
        }
    }
}
