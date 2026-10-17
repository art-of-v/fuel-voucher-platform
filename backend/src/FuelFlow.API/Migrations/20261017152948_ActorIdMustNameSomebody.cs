using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Makes "a user id that names nobody" unrepresentable on both D-13 tables.
    /// <para>
    /// <c>ck_voucher_events_actor_named</c> and <c>ck_voucher_movements_actor_named</c> say
    /// <c>(actor_kind = 'user') = (actor_user_id IS NOT NULL)</c>, which is what stops a person from
    /// being recorded without an id. It does not stop them being recorded with an id that resolves to
    /// no one, because the zero GUID is not NULL.
    /// </para>
    /// <para>
    /// That is not hypothetical: <c>AdminVoucherController.GetUserId</c> returns <c>Guid.Empty</c>
    /// when the <c>NameIdentifier</c> claim is missing, so <c>Guid.Empty</c> is what this database
    /// would actually have stored for an admin action with no authenticated actor. The first version of
    /// this schema claimed in a comment that the zero GUID was unrepresentable; it was not, and this
    /// constraint is what makes that comment true.
    /// </para>
    /// <para>
    /// No data backfill: production is pre-launch and both tables are new. The rule is stated as
    /// <c>IS NULL OR ...</c> so it cannot evaluate to NULL for a non-null id — the same trap that made a
    /// half-written expiry pair writable, and the reason it is its own constraint rather than an extra
    /// arm folded into the pairing.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class ActorIdMustNameSomebody : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_actor_is_somebody",
                table: "voucher_movements",
                sql: "actor_user_id IS NULL OR actor_user_id <> '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_actor_is_somebody",
                table: "voucher_events",
                sql: "actor_user_id IS NULL OR actor_user_id <> '00000000-0000-0000-0000-000000000000'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_actor_is_somebody",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_actor_is_somebody",
                table: "voucher_events");
        }
    }
}
