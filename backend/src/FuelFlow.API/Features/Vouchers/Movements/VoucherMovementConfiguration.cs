using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Movements;

/// <summary>
/// D-13. The append-only custody log that <c>voucher_renewal_items</c>, <c>voucher_exchanges</c> and
/// <c>operator_voucher_renewals</c> each kept their own fragment of.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shape is <c>voucher_id</c> plus <c>from_location</c> → <c>to_location</c>, and getting there
/// took two corrections.</b> The first draft of D-13 proposed <c>from_voucher_id NOT NULL</c> and
/// <c>to_voucher_id</c>, which asks two questions with two columns: which paper moved, and which paper
/// it became. The second is provenance, not movement, so every genuine handover wrote
/// <c>to_voucher_id = NULL</c> and put the real destination in <c>order_id</c>. A ledger row whose
/// second column is always empty is a ledger modelling the wrong thing.
/// </para>
/// <para>
/// Fixing it has a consequence worth stating: <c>extend</c> no longer needs a constraint to keep it
/// out. It moves no paper between places, so it simply is not a movement. Under the old shape it had to
/// be excluded by refusing <c>from = to</c>, which is a rule about the wrong thing.
/// </para>
/// <para>
/// <b>The voucher foreign key is RESTRICT</b>: paper that moved must not be deletable out from under
/// the record that it moved, the same rule <c>voucher_exchanges</c> already follows on both its
/// voucher sides. <b>order_id</b> and both actor columns carry none — an audit row must not block
/// deleting its own subject, which is exactly how <c>order_line_items.order_id</c> being RESTRICT
/// wedged <c>DeletedUnpaidOrderCleanupService</c>. The actor rule is §G of TARGET_SCHEMA, as four
/// tables in this schema already follow it.
/// </para>
/// </remarks>
internal sealed class VoucherMovementConfiguration : IEntityTypeConfiguration<VoucherMovement>
{
    public void Configure(EntityTypeBuilder<VoucherMovement> builder)
    {
        builder.ToTable("voucher_movements");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.BatchId)
            .HasColumnName("batch_id");

        // A movement may be part of a settlement or stand alone; RESTRICT rather than SetNull because a
        // movement must stay attributable to the agreement it belonged to.
        builder.HasOne<MovementBatch>()
            .WithMany()
            .HasForeignKey(e => e.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.Kind)
            .HasColumnName("kind")
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(e => e.VoucherId)
            .HasColumnName("voucher_id")
            .IsRequired();

        // Restrict: paper that moved must not be deletable, exactly as voucher_exchanges restricts on
        // old_voucher_id and new_voucher_id.
        builder.HasOne<FuelVoucher>()
            .WithMany()
            .HasForeignKey(e => e.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.FromLocation)
            .HasColumnName("from_location")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.ToLocation)
            .HasColumnName("to_location")
            .HasMaxLength(16)
            .IsRequired();

        // No FK on order_id, and no navigations to users or parties. See the remarks.
        builder.Property(e => e.OrderId)
            .HasColumnName("order_id");

        // Bare uuid, no FK: an audit row has to be writable whatever happened to the actor row, which is §G
        // of TARGET_SCHEMA and what voucher_exchanges.acting_user_id and
        // operator_voucher_renewals.acting_user_id are bare too.
        //
        // The DEFAULT is here so the migration can ADD this column to a table that already exists.
        // Every writer states the kind explicitly; an automatic action with no person behind it is
        // `system`, which is an answer rather than a missing author.
        builder.Property(e => e.ActorKind)
            .HasColumnName("actor_kind")
            .HasMaxLength(12)
            .HasDefaultValue(VoucherActorKinds.System)
            .IsRequired();

        // Bare uuid, no FK: a movement has to be writable whatever happened to the actor row, and the
        // movement must not be the thing that blocks deleting that account (planning #307,
        // TARGET_SCHEMA §G). The pairing constraint below is what says a 'user' actor is named.
        builder.Property(e => e.ActorUserId)
            .HasColumnName("actor_user_id");

        builder.Property(e => e.ActorPartyId)
            .HasColumnName("actor_party_id");

        builder.Property(e => e.AmountUah)
            .HasColumnName("amount_uah")
            .HasColumnType("numeric(12,2)");

        builder.Property(e => e.CostDeltaUah)
            .HasColumnName("cost_delta_uah")
            .HasColumnType("numeric(14,2)");

        builder.Property(e => e.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500);

        builder.Property(e => e.LineItemId)
            .HasColumnName("line_item_id");

        // The question the operator screen asks is "what happened to THIS voucher", and it reads
        // forwards through time, so the index carries occurred_at_utc with it.
        builder.HasIndex(e => new { e.VoucherId, e.OccurredAtUtc })
            .HasDatabaseName("ix_voucher_movements_voucher");

        builder.HasIndex(e => e.BatchId)
            .HasDatabaseName("ix_voucher_movements_batch")
            .HasFilter("batch_id IS NOT NULL");

        // Which movements a renewal order settled, without reading the whole order's movements.
        builder.HasIndex(e => e.LineItemId)
            .HasDatabaseName("ix_voucher_movements_line")
            .HasFilter("line_item_id IS NOT NULL");

        // Every movement a sale settled, for revenue and refund arithmetic.
        builder.HasIndex(e => e.OrderId)
            .HasDatabaseName("ix_voucher_movements_order")
            .HasFilter("order_id IS NOT NULL");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_voucher_movements_kind",
                $"kind IN ({string.Join(",", VoucherMovementKinds.All.Select(k => $"'{k}'"))})");

            t.HasCheckConstraint(
                "ck_voucher_movements_locations",
                $"from_location IN ({string.Join(",", VoucherLocations.All.Select(k => $"'{k}'"))}) AND to_location IN ({string.Join(",", VoucherLocations.All.Select(k => $"'{k}'"))})");

            // The row says a paper moved, so it must say it moved somewhere else. This is what
            // "extend is not a movement" now means structurally, rather than a rule about vouchers
            // happening to be the same row on both sides.
            t.HasCheckConstraint("ck_voucher_movements_distinct_locations",
                "from_location <> to_location");

            // Money in is money in: a negative customer payment is a sign error, not a refund. Refunds
            // are Payments rows (D-10), not negative movements.
            t.HasCheckConstraint("ck_voucher_movements_amount_non_negative",
                "amount_uah IS NULL OR amount_uah >= 0");

            // Every action has an author. A person needs an id; the scheduler does not and must not
            // invent one. This is what makes NULL unrepresentable.
            t.HasCheckConstraint(
                "ck_voucher_movements_actor_kind",
                $"actor_kind IN ({string.Join(",", VoucherActorKinds.All.Select(k => $"'{k}'"))})");

            t.HasCheckConstraint("ck_voucher_movements_actor_named",
                "(actor_kind = 'user') = (actor_user_id IS NOT NULL)");

            // The zero GUID is what a missing claim actually becomes, not a hypothetical:
            // `AdminVoucherController.GetUserId` returns `Guid.Empty` rather than null when the
            // NameIdentifier claim is absent. Being NOT NULL it satisfies the pairing constraint
            // above, so without this the row names a user that does not exist. The first version of
            // this table claimed otherwise; this is what makes the claim true.
            t.HasCheckConstraint("ck_voucher_movements_actor_is_somebody",
                "actor_user_id IS NULL OR actor_user_id <> '00000000-0000-0000-0000-000000000000'");
        });
    }
}
