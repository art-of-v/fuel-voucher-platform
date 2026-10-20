using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Gives the operator's renewal a key the database enforces.
    /// <para>
    /// A customer may renew the same voucher any number of times - that is the rule the
    /// business gave on 2026-10-20 - so there is deliberately no unique on the voucher alone.
    /// The key is (voucher, the expiry it was moved to), because a repeated request produces
    /// the SAME new expiry.
    /// <para>
    /// Why the database has to be the one enforcing it: the handler checks first, and the check
    /// and the write are not in the same atomic step, so a double-clicked "confirm renewal" is
    /// a race rather than a guarantee. The race is won by the index, not by the check. With
    /// <c>ck_operator_renewal_expiration_moves_forward</c> already in place, both halves are
    /// covered: the one where the voucher's expiry was already moved (the check catches
    /// new == old), and the one where it was not (this index catches the same target).
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class OperatorRenewalIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "ix_operator_voucher_renewals_voucher_id_new_expiration"
                ON "operator_voucher_renewals" ("voucher_id", "new_expiration");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "ix_operator_voucher_renewals_voucher_id_new_expiration"
                ON "operator_voucher_renewals" ("voucher_id", "new_expiration");
                """);
        }
    }
}
