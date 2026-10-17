using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Events;

/// <summary>
/// D-13. The state history that <c>voucher_movements</c> deliberately does not carry.
/// </summary>
/// <remarks>
/// <para>
/// Six CHECKs, and each of them prevents a specific state that is not merely ugly but wrong — a status
/// changed to itself, an extension with only one of its two dates, a type carrying details belonging to
/// a different type. The alternative is a log whose rows mean whatever the writer felt like that day,
/// which is the thing an audit trail must not be.
/// </para>
/// <para>
/// <b>The pairing CHECKs are the interesting ones.</b> <c>Extended</c> must carry both dates and they must
/// run forwards; everything else must carry neither. A renewal is written from the voucher's current
/// <c>customer_expiration_date</c>, which the same statement then overwrites, so a half-written pair is
/// not recoverable by any later reader. This is the same defect the order-line renewal snapshots
/// (<c>20261017065731</c>) were built to prevent, and it is checked the same way.
/// </para>
/// <para>
/// <b>The voucher FK is RESTRICT</b>, like every other reference to paper in this schema: a voucher's
/// history must not be deletable by deleting the voucher. <b>The actor columns are FK-free</b> per §G,
/// so an event stays writable when the user who caused it has gone.
/// </para>
/// </remarks>
internal sealed class VoucherEventConfiguration : IEntityTypeConfiguration<VoucherEvent>
{
    public void Configure(EntityTypeBuilder<VoucherEvent> builder)
    {
        builder.ToTable("voucher_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.VoucherId)
            .HasColumnName("voucher_id")
            .IsRequired();

        builder.HasOne<FuelVoucher>()
            .WithMany()
            .HasForeignKey(e => e.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(e => e.FromStatus)
            .HasColumnName("from_status")
            .HasMaxLength(32);

        builder.Property(e => e.ToStatus)
            .HasColumnName("to_status")
            .HasMaxLength(32);

        builder.Property(e => e.PreviousCustomerExpiration)
            .HasColumnName("previous_customer_expiration")
            .HasColumnType("date");

        builder.Property(e => e.NewCustomerExpiration)
            .HasColumnName("new_customer_expiration")
            .HasColumnType("date");

        builder.Property(e => e.ExternalRef)
            .HasColumnName("external_ref");

        // Bare uuid, no FK: an event has to stay writable whatever happened to the actor row, which is
        // §G of TARGET_SCHEMA and what order_events and the three settlement records already do.
        //
        // The DEFAULT is here so the migration can ADD this column to a table that already exists.
        // Every writer states the kind explicitly; an automatic action with no person behind it is
        // `system`, which is an answer rather than a missing author.
        builder.Property(e => e.ActorKind)
            .HasColumnName("actor_kind")
            .HasMaxLength(12)
            .HasDefaultValue(VoucherActorKinds.System)
            .IsRequired();

        builder.Property(e => e.ActorUserId)
            .HasColumnName("actor_user_id");

        builder.Property(e => e.ActorPartyId)
            .HasColumnName("actor_party_id");

        builder.Property(e => e.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500);

        builder.Property(e => e.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb");

        // The question this table exists to answer is "what happened to THIS voucher", and it reads
        // forwards through time, so occurred_at_utc rides along in the key.
        builder.HasIndex(e => new { e.VoucherId, e.OccurredAtUtc })
            .HasDatabaseName("ix_voucher_events_voucher");

        // "Show me everything that happened because of order X" is what turns a log into evidence.
        builder.HasIndex(e => e.ExternalRef)
            .HasDatabaseName("ix_voucher_events_ref")
            .HasFilter("external_ref IS NOT NULL");

        builder.ToTable(t =>
        {
            var statuses = "'" + string.Join("','", Enum.GetNames<VoucherStatus>()) + "'";

            t.HasCheckConstraint(
                "ck_voucher_events_type",
                $"event_type IN ({string.Join(",", VoucherEventTypes.All.Select(k => $"'{k}'"))})");

            // A misspelled status is worse than a null one: it is invisible until someone queries for the
            // typo. Pinned to the same nine values fuel_vouchers.status is CHECKed against.
            t.HasCheckConstraint("ck_voucher_events_statuses",
                $"(from_status IS NULL OR from_status IN ({statuses})) " +
                $"AND (to_status IS NULL OR to_status IN ({statuses}))");

            // A status change that changed nothing, or that says only where it ended up, is not a record
            // of anything. Both ends or neither.
            //
            // Written as `(is = 'X') = (both present)` rather than `(is <> 'X') = (both absent)` on
            // purpose. The latter evaluates to NULL for a half pair - `FALSE = FALSE` is TRUE, but the
            // "did it change" arm becomes `NULL > NULL`, and Postgres ACCEPTS a CHECK whose result is
            // NULL. That is a silent hole, not a loud one: `Extended` with only a previous date would
            // have been accepted.
            t.HasCheckConstraint("ck_voucher_events_status_changed",
                $"(event_type = 'StatusChanged') = (from_status IS NOT NULL AND to_status IS NOT NULL) " +
                $"AND (event_type <> 'StatusChanged' OR from_status <> to_status)");

            // The expiry history, same reasoning: whole or absent, and forwards. A renewal is written
            // from the voucher's current customer_expiration_date, which the same statement then
            // overwrites, so a half-written pair is not recoverable by anyone reading later.
            t.HasCheckConstraint("ck_voucher_events_expiration_pair",
                "(event_type = 'Extended') = (previous_customer_expiration IS NOT NULL AND new_customer_expiration IS NOT NULL) " +
                "AND (event_type <> 'Extended' OR new_customer_expiration > previous_customer_expiration)");

            // An Imported row describes a voucher coming into existence, so it cannot have been
            // something before. Stated rather than implied because "imported from status X" is exactly
            // what a half-finished import would write.
            t.HasCheckConstraint("ck_voucher_events_imported_has_no_predecessor",
                "event_type <> 'Imported' OR (from_status IS NULL AND previous_customer_expiration IS NULL)");

            // Every action has an author, same rule as voucher_movements. A person needs an id; the
            // scheduler does not and must not invent one. This makes NULL unrepresentable.
            t.HasCheckConstraint(
                "ck_voucher_events_actor_kind",
                $"actor_kind IN ({string.Join(",", VoucherActorKinds.All.Select(k => $"'{k}'"))})");

            t.HasCheckConstraint("ck_voucher_events_actor_named",
                "(actor_kind = 'user') = (actor_user_id IS NOT NULL)");

            // The zero GUID is what a missing claim actually becomes, not a hypothetical:
            // `AdminVoucherController.GetUserId` returns `Guid.Empty` rather than null when the
            // NameIdentifier claim is absent. That value is NOT NULL, so the pairing CHECK above
            // accepts it — and the row then names a user that does not exist, which is precisely the
            // "plausible-looking value that resolves to nobody" this whole arrangement exists to
            // prevent. The first version of this table claimed the zero GUID was unrepresentable and
            // it was not; this is what makes that claim true.
            //
            // Stated as its own constraint rather than folded into the pairing, so each rule reads
            // separately. `IS NULL OR ...` also cannot evaluate to NULL for a non-null id, so this
            // half does not repeat the NULL trap above.
            t.HasCheckConstraint("ck_voucher_events_actor_is_somebody",
                "actor_user_id IS NULL OR actor_user_id <> '00000000-0000-0000-0000-000000000000'");
        });
    }
}
