using FuelFlow.Features.Orders.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Orders.Configurations;

internal sealed class OrderLineItemConfiguration : IEntityTypeConfiguration<OrderLineItem>
{
    public void Configure(EntityTypeBuilder<OrderLineItem> builder)
    {
        builder.ToTable("order_line_items");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id);

        builder.Property(e => e.OrderId)
            .IsRequired();

        builder.Property(e => e.Provider)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.FuelTypeId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Liters)
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(e => e.Quantity)
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(e => e.UnitPrice)
            .IsRequired();

        builder.Property(e => e.LineTotal)
            .IsRequired();

        builder.HasOne(e => e.Order)
            .WithMany(o => o.LineItems)
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.OrderId);
    }
}
