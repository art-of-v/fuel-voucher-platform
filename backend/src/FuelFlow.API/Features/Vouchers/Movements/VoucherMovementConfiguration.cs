using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Movements;

/// <summary>
/// D-13. The one append-only ledger <c>voucher_renewal_items</c>, <c>voucher_exchanges</c> and
/// <c>operator_voucher_renewals</c> were three private copies of.
/// </summary>
/// <remarks>
/// <para>
/// Three decisions here would each have been written the other way by the TARGET_SCHEMA draft, and each
/// was checked against the shipped schema before being changed.
/// </para>
/// <para>
/// <b><c>from_voucher_id</c> is nullable.</b> <c>ImportVouchersCommand</c> creates the majority of
/// vouchers in this system and a voucher arriving from a supplier PDF has no predecessor, so a
/// NOT NULL here made the largest class of movement unrepresentable. What is required instead is that
/// a row names <i>something</i>: <c>ck_voucher_movements_has_a_side</c>.
/// </para>
/// <para>
/// <b>The voucher foreign keys are RESTRICT; <c>order_id</c> and both actor columns have none.</b> A
/// voucher that took part in a movement must not be deletable — that is the same rule
/// <c>voucher_exchanges</c> already follows on both of its voucher sides. But an audit row must not be
/// the thing that blocks deleting its own subject either, and that is what an FK on <c>order_id</c>
/// makes it: <c>order_line_items.order_id</c> is RESTRICT, and that already forced
/// <c>DeletedUnpaidOrderCleanupService</c> to remove its lines before the order. The actor columns
/// follow §G of TARGET_SCHEMA and the four tables in this schema that already keep them FK-free.
/// </para>
/// <para>
/// <b>The enum is checked, not trusted.</b> Nineteen kinds is a lot of surface for a
/// <c>varchar(24)</c>, and an unrecognised word arriving here later would silently be a movement
/// nobody can interpret. The CHECK is what makes the set closed.
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

        // A movement may be part of a settlement or stand alone; it is RESTRICT rather than SetNull
        // because a movement must stay attributable to the agreement it belonged to.
        builder.HasOne<MovementBatch>()
            .WithMany()
            .HasForeignKey(e => e.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.Kind)
            .HasColumnName("kind")
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(e => e.FromVoucherId)
            .HasColumnName("from_voucher_id");

        builder.Property(e => e.ToVoucherId)
            .HasColumnName("to_voucher_id");

        // Restrict on the vouchers: paper that moved must not be deletable out from under the record
        // that it moved, exactly as voucher_exchanges restricts on old_voucher_id and new_voucher_id.
        builder.HasOne<FuelVoucher>()
            .WithMany()
            .HasForeignKey(e => e.FromVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FuelVoucher>()
            .WithMany()
            .HasForeignKey(e => e.ToVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        // No FK on order_id, and no navigations to users or parties. See the remarks.
        builder.Property(e => e.OrderId)
            .HasColumnName("order_id");

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

        // Every question the operator screen asks is "what happened to THIS voucher", so both sides
        // are indexed and both are ordered newest-first: a voucher's life is read forwards.
        builder.HasIndex(e => new { e.FromVoucherId, e.OccurredAtUtc })
            .HasDatabaseName("ix_voucher_movements_from");

        builder.HasIndex(e => e.ToVoucherId)
            .HasDatabaseName("ix_voucher_movements_to")
            .HasFilter("to_voucher_id IS NOT NULL");

        builder.HasIndex(e => e.BatchId)
            .HasDatabaseName("ix_voucher_movements_batch")
            .HasFilter("batch_id IS NOT NULL");

        // Which movements a renewal order settled, without reading the whole order's movements.
        builder.HasIndex(e => e.LineItemId)
            .HasDatabaseName("ix_voucher_movements_line")
            .HasFilter("line_item_id IS NOT NULL");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_voucher_movements_kind",
                $"kind IN ({string.Join(",", VoucherMovementKinds.All.Select(k => $"'{k}'"))})");

            // A movement with neither side names nothing and means nothing. This is the check that
            // replaces the NOT NULL the first draft put on from_voucher_id.
            t.HasCheckConstraint("ck_voucher_movements_has_a_side",
                "from_voucher_id IS NOT NULL OR to_voucher_id IS NOT NULL");

            // A voucher cannot move to itself. This is also what makes extend unrepresentable here,
            // which is the point: extending an expiry moves no voucher, and it is a voucher_events
            // state change instead.
            t.HasCheckConstraint("ck_voucher_movements_distinct_sides",
                "from_voucher_id IS DISTINCT FROM to_voucher_id");

            // Money in is money in: a negative customer payment is a sign error, not a refund. Refunds
            // are Payments rows (D-10), not negative movements.
            t.HasCheckConstraint("ck_voucher_movements_amount_non_negative",
                "amount_uah IS NULL OR amount_uah >= 0");
        });
    }
}
