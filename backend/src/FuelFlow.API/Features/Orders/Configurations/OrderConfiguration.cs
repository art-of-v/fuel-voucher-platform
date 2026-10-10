using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Orders.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(e => e.LegalEntityId)
            .HasColumnName("legal_entity_id");

        builder.Property(e => e.Price)
            .HasColumnName("price")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(OrderKind.Purchase)
            .IsRequired();

        builder.Property(e => e.SourceOrderId)
            .HasColumnName("source_order_id");

        builder.Property(e => e.MonobankInvoiceId)
            .HasColumnName("monobank_invoice_id")
            .HasMaxLength(100);

        builder.Property(e => e.MonobankStatus)
            .HasColumnName("monobank_status")
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.MonobankMerchant)
            .HasColumnName("monobank_merchant")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.MonobankPaymentUrl)
            .HasColumnName("monobank_payment_url")
            .HasMaxLength(500);

        builder.Property(e => e.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(150);

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.Property(e => e.FulfilledAtUtc)
            .HasColumnName("fulfilled_at_utc");

        builder.Property(e => e.PartiallyFulfilledSinceUtc)
            .HasColumnName("partially_fulfilled_since_utc");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasQueryFilter(e => !e.IsDeleted);

        builder.HasOne<FuelFlow.Features.Contracts.SharedModels.LegalEntity>()
            .WithMany()
            .HasForeignKey(e => e.LegalEntityId)
            .OnDelete(DeleteBehavior.SetNull);

        // Self-reference for issuances: an issuance points at the purchase its fuel came from.
        // SetNull rather than Cascade so deleting one order can never cascade into another.
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(e => e.SourceOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.LegalEntityId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => e.SourceOrderId);
        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL");
        builder.HasIndex(e => new { e.UserId, e.Status });
        builder.HasIndex(e => new { e.UserId, e.CreatedAtUtc })
            .IsDescending(false, true);
    }
}
