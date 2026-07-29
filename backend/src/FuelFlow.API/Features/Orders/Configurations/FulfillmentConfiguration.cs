using FuelFlow.Features.Orders.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Orders.Configurations;

internal sealed class FulfillmentConfiguration : IEntityTypeConfiguration<Fulfillment>
{
    public void Configure(EntityTypeBuilder<Fulfillment> builder)
    {
        builder.ToTable("fulfillments");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id);

        builder.Property(e => e.OrderId)
            .IsRequired();

        builder.Property(e => e.VoucherId)
            .IsRequired();

        builder.Property(e => e.FulfilledAtUtc)
            .IsRequired();

        builder.HasIndex(e => e.OrderId);
        builder.HasIndex(e => e.VoucherId);
    }
}
