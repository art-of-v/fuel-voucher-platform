using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Payments.Configurations;

internal sealed class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable(
            "payment_allocations",
            t => t.HasComment(
                "How much of one held payment pays for one order. The table that makes a declared transfer a balance "
                + "rather than a voucher for exactly one sale."));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.PaymentId)
            .HasColumnName("payment_id")
            .IsRequired()
            .HasComment(
                "The money drawn on. At write time it is a succeeded capture naming no order; the trigger " +
                "assert_payment_draws_on_held_money refuses any other row, because a payment already attached to an " +
                "order is counted on that order in full and a second claim on it would be the same banknote twice.");

        builder.Property(e => e.OrderId)
            .HasColumnName("order_id")
            .IsRequired()
            .HasComment(
                "The sale this much money is answering for. Not a copy of payments.order_id: that column is the whole " +
                "charge on one order, and it cannot say that a transfer covered part of this one and part of another.");

        builder.Property(e => e.AmountUah)
            .HasColumnName("amount_uah")
            .HasColumnType("numeric(12,2)")
            .IsRequired()
            .HasComment(
                "The slice drawn. Same type as orders.price and payments.amount_uah, so the money gate adds the three " +
                "without a conversion. The rows sharing a payment never sum above its amount - that is a trigger, " +
                "because a CHECK cannot read another table.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired()
            .HasComment(
                "When the platform decided. The money's own date stays on payments.paid_at_utc: when the transfer " +
                "arrived and when it was assigned to a sale are two facts, and a report that conflates them says the " +
                "company held money it did not yet hold.");

        builder.HasOne(e => e.Payment)
            .WithMany(p => p.Allocations)
            .HasForeignKey(e => e.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, like every other money-side FK here: an allocation is the reason an order is paid. If the
        // order could be purged underneath it, the payment would go on looking spent with nothing to show for
        // it, and the balance would be short by money that is really in the account.
        builder.HasOne(e => e.Order)
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // The trigger's read, and the operator's "what is left of this transfer". Both sum the rows of one
        // payment, and the trigger takes a lock on that payment before it sums - which only helps while the
        // sum is an index scan.
        builder.HasIndex(e => e.PaymentId)
            .HasDatabaseName("ix_payment_allocations_payment");

        // The money gate reads this for every settle and every fulfilment retry.
        builder.HasIndex(e => e.OrderId)
            .HasDatabaseName("ix_payment_allocations_order");

        // Zero is not a draw, and a negative one would be a refund - which is a row of kind 'Refund' on
        // payments, not a claim on this table.
        builder.ToTable(t => t.HasCheckConstraint("ck_payment_allocations_amount_positive", "amount_uah > 0"));
    }
}
