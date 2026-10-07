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
            .IsRequired();

        builder.Property(e => e.VoucherId)
            .HasColumnName("voucher_id")
            .IsRequired();

        builder.Property(e => e.FulfilledAtUtc)
            .HasColumnName("fulfilled_at_utc")
            .IsRequired();

        builder.HasIndex(e => e.OrderId);
        builder.HasIndex(e => e.VoucherId);

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
    }
}
