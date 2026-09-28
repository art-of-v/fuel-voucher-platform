using FuelFlow.Features.Orders.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Orders.Configurations;

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(e => e.Amount)
            .HasColumnName("amount")
            .IsRequired();

        builder.Property(e => e.InvoiceId)
            .HasColumnName("invoice_id")
            .HasMaxLength(100)
            .IsRequired();

        // 150, not 100: ext_ref stores a copy of the order's IdempotencyKey, whose column
        // (orders.idempotency_key) is varchar(150). A bulk checkout builds a 111-char key
        // (userId + time bucket + cart digest + a fresh GUID), so refunding such an order
        // overflowed the old varchar(100) and threw Postgres 22001 → an unhandled 500.
        builder.Property(e => e.ExtRef)
            .HasColumnName("ext_ref")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.MonobankStatus)
            .HasColumnName("monobank_status")
            .HasMaxLength(30);

        builder.Property(e => e.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(500);

        builder.Property(e => e.CreatedByUserId)
            .HasColumnName("created_by_user_id");

        builder.Property(e => e.CreatedByUserName)
            .HasColumnName("created_by_user_name")
            .HasMaxLength(200);

        builder.Property(e => e.IsAutomatic)
            .HasColumnName("is_automatic")
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.HasIndex(e => e.OrderId)
            .IsUnique();

        builder.HasOne(e => e.Order)
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
