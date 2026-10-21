using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Movements;

/// <summary>
/// D-13. The append-only custody log that <c>voucher_renewal_items</c>, the exchange pairs and
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
/// the record that it moved, the same rule the exchange's retired paper followed. <b>order_id</b> and
/// both actor columns carry none — an audit row must not block
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
            .HasColumnName("batch_id")
            .HasComment(                "The settlement this movement is part of, when there was one. NULL for a movement an" +
                    "operator or a customer performed on its own.");

        // A movement may be part of a settlement or stand alone; RESTRICT rather than SetNull because a
        // movement must stay attributable to the agreement it belonged to.
        builder.HasOne<MovementBatch>()
            .WithMany()
            .HasForeignKey(e => e.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.Kind)
            .HasColumnName("kind")
            .HasMaxLength(24)
            .IsRequired()
            .HasComment(                "What happened - issued, redeemed, blocked, released, and the rest. See" +
                    "VoucherMovementKinds.");

        builder.Property(e => e.VoucherId)
            .HasColumnName("voucher_id")
            .IsRequired()
            .HasComment(                "The piece of paper that moved.");

        // Restrict: paper that moved must not be deletable, exactly as the exchange's retired paper
        // was protected.
        builder.HasOne<FuelVoucher>()
            .WithMany()
            .HasForeignKey(e => e.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.FromLocation)
            .HasColumnName("from_location")
            .HasMaxLength(16)
            .IsRequired()
            .HasComment(                "Where it was. See VoucherLocations.");

        builder.Property(e => e.ToLocation)
            .HasColumnName("to_location")
            .HasMaxLength(16)
            .IsRequired()
            .HasComment(                "Where it is now. Never equal to from_location - that is" +
                    "ck_voucher_movements_distinct_locations, and a row that says a paper moved from the" +
                    "shelf to the shelf is not a movement.");

        // No FK on order_id, and no navigations to users or parties. See the remarks.
        builder.Property(e => e.OrderId)
            .HasColumnName("order_id")
            .HasComment(                "The order this movement settled, when a customer paid for it. A bare uuid, no foreign" +
                    "key: an audit row must not be the thing that blocks deleting its own subject.");

        // Bare uuid, no FK: an audit row has to be writable whatever happened to the actor row, which is §G
        // of TARGET_SCHEMA and what operator_voucher_renewals.acting_user_id is bare too.
        //
        // The DEFAULT is here so the migration can ADD this column to a table that already exists.
        // Every writer states the kind explicitly; an automatic action with no person behind it is
        // `system`, which is an answer rather than a missing author.
        builder.Property(e => e.ActorKind)
            .HasColumnName("actor_kind")
            .HasMaxLength(12)
            .HasDefaultValue(VoucherActorKinds.System)
            .IsRequired()
            .HasComment(                "Who caused it: a person, or the scheduler. See VoucherActorKinds.");

        // Bare uuid, no FK: a movement has to be writable whatever happened to the actor row, and the
        // movement must not be the thing that blocks deleting that account (planning #307,
        // TARGET_SCHEMA §G). The pairing constraint below is what says a 'user' actor is named.
        builder.Property(e => e.ActorUserId)
            .HasColumnName("actor_user_id")
            .HasComment(                "The person who caused it, required when actor_kind is 'user'. A bare uuid, no foreign" +
                    "key - the reason every other audit record in this schema gives.");

        builder.Property(e => e.ActorPartyId)
            .HasColumnName("actor_party_id")
            .HasComment(                "The company that caused the movement, when it was a company rather than a user. A bare" +
                    "uuid.");

        builder.Property(e => e.AmountUah)
            .HasColumnName("amount_uah")
            .HasColumnType("numeric(12,2)")
            .HasComment(                "What the customer paid for this movement, in UAH. NULL when money did not move - most" +
                    "movements are the operator's own housekeeping. NOT the batch total: a settlement's total" +
                    "lives on movement_batches.surcharge_uah.");

        builder.Property(e => e.CostDeltaUah)
            .HasColumnName("cost_delta_uah")
            .HasColumnType("numeric(14,2)")
            .HasComment(                "What this movement did to supplier-side cost, in UAH. NULL when cost did not move." +
                    "Recorded AT THE TIME, from the values written in the same transaction, because it cannot" +
                    "be derived afterwards: fuel_vouchers.cost_per_liter is mutable and gets overwritten, so" +
                    "'what did this voucher cost before' stops existing the moment the column is restamped.");

        builder.Property(e => e.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired()
            .HasComment(                "When it happened.");

        builder.Property(e => e.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500)
            .HasComment(                "Why, in the operator's words.");

        builder.Property(e => e.LineItemId)
            .HasColumnName("line_item_id")
            .HasComment(                "The order line this movement settled, for the renewal paths.");

        // D-13, planning #319: the pairing the exchange pairs kept as old_voucher_id ->
        // new_voucher_id, folded into the ledger. The row of the RETIRED paper carries it, so the
        // operator's question "what happened to THIS voucher" is answered by this voucher's own rows.
        builder.Property(e => e.ReplacedByVoucherId)
            .HasColumnName("replaced_by_voucher_id")
            .HasComment("The paper that took this voucher's place, when this paper was traded for another one. NULL for a movement that was not a trade - a sale, a return, an import. The row of the retired paper carries it, not the row of the successor.");

        // Restrict: the paper that replaced this one must not be deletable out from under the record
        // of the trade - the same rule the exchange's replacement side followed.
        builder.HasOne<FuelVoucher>()
            .WithMany()
            .HasForeignKey(e => e.ReplacedByVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.SurchargeShareUah)
            .HasColumnName("surcharge_share_uah")
            .HasColumnType("numeric(12,2)")
            .HasComment("This movement's share of the money a settlement moved: the doplata the operator paid the supplier for this pair of paper. NOT the settlement total - that lives on movement_batches.surcharge_uah, and the shares can sum to less than it.");

        builder.Property(e => e.AppliedCostPerLiter)
            .HasColumnName("applied_cost_per_liter")
            .HasColumnType("numeric(12,4)")
            .HasComment("The cost per litre stamped on the replacement when it arrived. A level, not a delta. Recorded AT THE TIME because it cannot be derived afterwards: fuel_vouchers.cost_per_liter is mutable and gets restamped.");

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

        // Which paper replaced this one, without reading the whole ledger. The question the operator
        // asks about a retired voucher - "what did it become" - is asked per voucher, not per batch.
        builder.HasIndex(e => e.ReplacedByVoucherId)
            .HasDatabaseName("ix_voucher_movements_replaced_by")
            .HasFilter("replaced_by_voucher_id IS NOT NULL");

        // Planning #319: a piece of paper goes to the supplier at most once. This is the folded form
        // of the UNIQUE index the exchange pairs used to carry on their retired voucher - the database
        // half of "a voucher cannot be exchanged twice", which the confirm's own guard only checks.
        //
        // Partial, because the rule is about the trade and not about the paper: the same voucher has
        // plenty of other movements (issued, returned, gift, recall) and those are not the trade.
        builder.HasIndex(e => e.VoucherId)
            .HasDatabaseName("ix_voucher_movements_exchange_out")
            .IsUnique()
            .HasFilter("kind = 'exchange_out'");

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

            // A trade is between two pieces of paper. A voucher that replaced itself is not a trade,
            // and the rule that says so is the same one the exchange's own pair followed.
            t.HasCheckConstraint("ck_voucher_movements_not_self_replaced",
                "replaced_by_voucher_id IS NULL OR replaced_by_voucher_id <> voucher_id");

            // The doplata is what the operator paid. A negative share is a sign error, and a refund
            // of a doplata is a Payment row (D-10), not a negative movement.
            t.HasCheckConstraint("ck_voucher_movements_surcharge_share_non_negative",
                "surcharge_share_uah IS NULL OR surcharge_share_uah >= 0");

            // Cost per litre is a price. A negative price is a sign error, not a discount.
            t.HasCheckConstraint("ck_voucher_movements_applied_cost_non_negative",
                "applied_cost_per_liter IS NULL OR applied_cost_per_liter >= 0");
        });
    }
}
