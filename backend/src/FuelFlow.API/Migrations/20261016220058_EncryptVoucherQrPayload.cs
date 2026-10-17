using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class EncryptVoucherQrPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Order matters and it is the whole trick.
            //
            // The fingerprint has to be computed from the plaintext while the column is still
            // plaintext, and it has to exist before the unique index. Adding it NOT NULL with a
            // constant default - the obvious order - cannot work on a populated table: every row
            // would get the same bytes and the unique index would fail with 23505. That is why this
            // looks long.
            //
            // Production holds zero fuel_vouchers, so in the deployment that matters every step below
            // is a no-op on the data. The backfill is here so the migration is correct rather than
            // correct-by-accident, and so a database restored from a backup can be migrated forward
            // without re-importing every voucher from the supplier.
            migrationBuilder.AddColumn<byte[]>(
                name: "qr_payload_sha256",
                table: "fuel_vouchers",
                type: "bytea",
                nullable: true);

            // Backfill while qr_payload is still text. sha256() is built into PostgreSQL 11+, so this
            // needs no extension - which matters, because the encryption itself is done in the
            // application rather than by pgcrypto.
            migrationBuilder.Sql("""
                UPDATE fuel_vouchers
                   SET qr_payload_sha256 = sha256(convert_to(qr_payload, 'UTF8'));
                """);

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_qr_payload",
                table: "fuel_vouchers");

            // Raw SQL rather than AlterColumn<byte[]>, because there is no implicit cast from varchar
            // to bytea and PostgreSQL rejects the statement at parse time - even on an empty table,
            // where there is nothing to convert. `::bytea` preserves the bytes as they are; the
            // application encrypts on the next write and on read, so what this produces for existing
            // rows is plaintext-in-a-typed-column rather than ciphertext.
            //
            // That is a real limitation, stated rather than hidden: rows that existed before this
            // migration are readable only by a context whose cipher is bypassed. Production has none.
            // A database with vouchers should re-import them from the supplier rather than rely on it.
            migrationBuilder.Sql("""
                ALTER TABLE fuel_vouchers
                    ALTER COLUMN qr_payload TYPE bytea USING qr_payload::bytea;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE fuel_vouchers
                    ALTER COLUMN qr_payload_sha256 SET NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_qr_payload_sha256",
                table: "fuel_vouchers",
                column: "qr_payload_sha256",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_qr_payload_sha256",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "qr_payload_sha256",
                table: "fuel_vouchers");

            migrationBuilder.Sql("""
                ALTER TABLE fuel_vouchers
                    ALTER COLUMN qr_payload TYPE varchar(2000) USING encode(qr_payload, 'base64');
                """);

            // Rolling back does not un-encrypt: the key is not in the migration. base64 is used so the
            // statement is valid - the bytes come back as text nobody can read, which is a
            // re-encrypted-looking blob rather than an empty string silently overwriting every
            // voucher QR. One-way by design.

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_qr_payload",
                table: "fuel_vouchers",
                column: "qr_payload",
                unique: true,
                filter: "is_deleted = false");
        }
    }
}
