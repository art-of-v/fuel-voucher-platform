using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
/// Closes a silent hole in <c>voucher_events</c>'s pairing constraints, found by its own tests.
/// </summary>
/// <para>
/// <b>Postgres accepts a CHECK whose result is NULL.</b> That is not a subtlety to remember, it is a
/// constraint that does not constrain. The first version of both pairings was written as
/// <c>(event_type &lt;&gt; 'Extended') = (previous IS NULL AND new IS NULL)</c>, which for a
/// <b>half</b> pair evaluates as follows: <c>FALSE = FALSE</c> is TRUE, so the first arm passes; the
/// second arm becomes <c>NULL &gt; date</c>, i.e. NULL; and <c>TRUE AND NULL</c> is NULL, which the
/// database accepts.
/// </para>
/// <para>
/// The consequence was exactly the state these constraints exist to prevent:
/// <c>Extended</c> with only a <c>previous_customer_expiration</c> and no new one was accepted. That is
/// a half-written expiry history for a renewal whose <c>customer_expiration_date</c> was overwritten by
/// the same statement — unrecoverable by anyone reading afterwards, and invisible until someone
/// queries for it. The same applied to a <c>StatusChanged</c> with one status instead of both.
/// </para>
/// <para>
/// <b>The fix is to make the first arm a definite TRUE or FALSE</b> rather than something that can
/// evaluate to NULL: <c>(event_type = 'Extended') = (previous IS NOT NULL AND new IS NOT NULL)</c>.
/// For a half pair that is <c>TRUE = FALSE</c>, i.e. FALSE, and <c>FALSE AND NULL</c> is FALSE — which
/// the database rejects. A second arm that can be NULL is then harmless, because it is always ANDed
/// with one that cannot.
/// </para>
/// <para>
/// Kept as its own migration rather than folded into the create, because the create is the one this PR
/// introduces and the migration history is append-only.
/// </para>
/// </summary>
    public partial class CloseTheNullHoleInVoucherEventPairing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_expiration_pair",
                table: "voucher_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_status_changed",
                table: "voucher_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_expiration_pair",
                table: "voucher_events",
                sql: "(event_type = 'Extended') = (previous_customer_expiration IS NOT NULL AND new_customer_expiration IS NOT NULL) AND (event_type <> 'Extended' OR new_customer_expiration > previous_customer_expiration)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_status_changed",
                table: "voucher_events",
                sql: "(event_type = 'StatusChanged') = (from_status IS NOT NULL AND to_status IS NOT NULL) AND (event_type <> 'StatusChanged' OR from_status <> to_status)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_expiration_pair",
                table: "voucher_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_status_changed",
                table: "voucher_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_expiration_pair",
                table: "voucher_events",
                sql: "(event_type <> 'Extended') = (previous_customer_expiration IS NULL AND new_customer_expiration IS NULL) AND (event_type <> 'Extended' OR new_customer_expiration > previous_customer_expiration)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_status_changed",
                table: "voucher_events",
                sql: "(event_type <> 'StatusChanged') = (from_status IS NULL AND to_status IS NULL) AND (event_type <> 'StatusChanged' OR from_status <> to_status)");
        }
    }
}
