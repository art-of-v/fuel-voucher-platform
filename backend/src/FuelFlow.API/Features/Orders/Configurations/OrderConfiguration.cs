using FuelFlow.Features.Orders.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Orders.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id);

        builder.Property(e => e.UserId)
            .IsRequired();

        builder.Property(e => e.Price)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.MonobankInvoiceId)
            .HasMaxLength(100);

        builder.Property(e => e.MonobankStatus)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.MonobankPaymentUrl)
            .HasColumnName("monobank_payment_url")
            .HasMaxLength(500);

        builder.Property(e => e.IdempotencyKey)
            .HasMaxLength(100);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .IsRequired();

        builder.Property(e => e.FulfilledAtUtc);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL");
        builder.HasIndex(e => new { e.UserId, e.Status });
        builder.HasIndex(e => new { e.UserId, e.CreatedAtUtc })
            .IsDescending(false, true);
    }
}
