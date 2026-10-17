using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
/// No recorded action may be without an author.
/// </summary>
/// <para>
/// <b>The rule.</b> Every row in <c>voucher_movements</c> and <c>voucher_events</c> names who caused it,
/// and the database refuses one that does not. This is not a convention a reviewer checks — conventions
/// about audit rows erode, and the first author-less row is the one nobody notices.
/// </para>
/// <para>
/// <b>Why a kind rather than a non-null column.</b> The actor id is deliberately FK-free (§G), so a bare
/// <c>uuid</c> can hold anything: NULL, a zero GUID, or a sentinel that looks like a user and resolves
/// to nobody. "Always record the actor" against that column is unenforceable. Splitting it into "is this
/// a person or the scheduler" plus "the person's id, required when it is a person" makes all three
/// unrepresentable — and the second CHECK is written as <c>(actor_kind = 'user') = (actor_user_id IS NOT NULL)</c>
/// rather than the more natural negated form, because <b>Postgres accepts a CHECK that evaluates to
/// NULL</b>. See <c>CloseTheNullHoleInVoucherEventPairing</c> for what that costs.
/// </para>
/// <para>
/// <b>The DEFAULT is not a soft default.</b> <c>'system'</c> exists so this migration can ADD the column
/// to tables that already exist. It is also the honest answer for the writes that genuinely have no
/// person behind them — the expiry sweep, the litres-mismatch repair, the retention jobs — which is an
/// author an operator can read, rather than a missing one. Every writer in the code states the kind
/// explicitly, and a test asserts the enum is exactly two words.
/// </para>
/// <para>
/// The timestamp half of the rule was already guaranteed: <c>occurred_at_utc</c> is NOT NULL on both
/// tables and every writer sets it.
/// </para>
/// </summary>
    public partial class EveryRecordedActionHasAnAuthor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actor_kind",
                table: "voucher_movements",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "system");

            migrationBuilder.AddColumn<string>(
                name: "actor_kind",
                table: "voucher_events",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "system");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_actor_kind",
                table: "voucher_movements",
                sql: "actor_kind IN ('user','system')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_actor_named",
                table: "voucher_movements",
                sql: "(actor_kind = 'user') = (actor_user_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_actor_kind",
                table: "voucher_events",
                sql: "actor_kind IN ('user','system')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_events_actor_named",
                table: "voucher_events",
                sql: "(actor_kind = 'user') = (actor_user_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_actor_kind",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_actor_named",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_actor_kind",
                table: "voucher_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_events_actor_named",
                table: "voucher_events");

            migrationBuilder.DropColumn(
                name: "actor_kind",
                table: "voucher_movements");

            migrationBuilder.DropColumn(
                name: "actor_kind",
                table: "voucher_events");
        }
    }
}
