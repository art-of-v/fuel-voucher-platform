using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Orders.Configurations;

internal sealed class FulfillmentConfiguration : IEntityTypeConfiguration<Fulfillment>
{
    public void Configure(EntityTypeBuilder<Fulfillment> builder)
    {
        builder.ToTable("fulfillments");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.OrderId)
            .HasColumnName("order_id")
            .IsRequired().HasComment("The order that delivered this voucher.");

        builder.Property(e => e.VoucherId)
            .HasColumnName("voucher_id")
            .IsRequired().HasComment("The voucher that was handed over.");

        builder.Property(e => e.LineItemId)
            .HasColumnName("line_item_id").HasComment("Which line of the order this voucher satisfied. NULL is the honest state of the data: a" +
                    "fulfilment without it says only that the order delivered a voucher, and the" +
                    "reconciliation cannot tell two lines of the same volume apart - which is how an order for" +
                    "'OKKO A95 10 L x2' and 'WOG A95 10 L x1' can pass while the customer is short a line.");

        // The reconciliation compares what each line asked for against what it got, so it reads by
        // line. Partial on the live rows for the same reason order_id is: a released fulfillment is
        // not part of the count, and indexing those would only ever add write amplification.
        builder.HasIndex(e => e.LineItemId)
            .HasFilter("released_at_utc IS NULL")
            .HasDatabaseName("ix_fulfillments_line_item_id");

        builder.Property(e => e.FulfilledAtUtc)
            .HasColumnName("fulfilled_at_utc")
            .IsRequired().HasComment("When the voucher was handed over.");

        builder.Property(e => e.ReleasedAtUtc)
            .HasColumnName("released_at_utc").HasComment("Set when the reconciliation takes the voucher back off this order, NULL while the delivery" +
                    "stands. The row is kept either way: the fulfilment records that a delivery happened, and" +
                    "the correction is the fact that it was undone - not a reason to erase the first part.");

        builder.Property(e => e.ReleaseReason)
            .HasColumnName("release_reason")
            .HasMaxLength(64).HasComment("Why the voucher was returned to stock. NULL while the delivery stands.");

        // The reconciler queries by order and counts what it finds; released rows must drop out of
        // that count or it would "correct" the same order forever. A global filter rather than a
        // `.Where` at each of the five read sites, because forgetting one would either re-release an
        // already-released voucher or leave a stock voucher looking delivered.
        builder.HasQueryFilter(e => e.ReleasedAtUtc == null);
        builder.HasIndex(e => e.OrderId)
            .HasFilter("released_at_utc IS NULL");
        builder.HasIndex(e => e.VoucherId);

        // Every release is a thing an operator may need to see. This is the only query that reports
        // them, so nothing else is lost.
        builder.HasIndex(e => e.ReleasedAtUtc)
            .HasFilter("released_at_utc IS NOT NULL");

        // The order owns the fulfillment: it is the row that says "this order delivered this voucher",
        // and the wallet nests vouchers by reading exactly these rows. Cascade, because a fulfillment
        // has no meaning without its order - the alternative is a row pointing at an order that no
        // longer exists, which hides a customer's vouchers from the wallet with nothing to explain it.
        //
        // Declared with string names rather than lambdas: with a lambda EF matches the navigation by
        // convention and scaffolds a SECOND, shadow foreign key column (`OrderId1`) instead of using
        // the existing `order_id`, because `Fulfillment` carries no `Order` navigation of its own while
        // `Order` already has the `Fulfillments` collection.
        builder
            .HasOne(nameof(Fulfillment.Order))
            .WithMany("Fulfillments")
            .HasForeignKey("OrderId")
            .HasConstraintName("FK_fulfillments_orders_order_id")
            .OnDelete(DeleteBehavior.Cascade);

        // The voucher, deliberately the other way round: a fulfillment is the record that a voucher was
        // handed out under an order, so the voucher must not disappear while a row still claims it was
        // delivered. `fuel_vouchers.order_id` restricts for the same reason from the order's side.
        builder.HasOne(e => e.Voucher)
            .WithMany()
            .HasForeignKey(e => e.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        // The line, Restrict like the voucher: "this line was satisfied by this voucher" is a fact
        // about a delivery, and it must not vanish because the line it satisfied was deleted. There is
        // no code path that deletes a line item, so this never fires in normal operation - which is
        // the point, the same reason refunds.order_id restricts.
        builder.HasOne<OrderLineItem>()
            .WithMany()
            .HasForeignKey(e => e.LineItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
