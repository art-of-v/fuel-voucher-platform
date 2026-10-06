using FuelFlow.Features.Vouchers.Renewal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Renewal;

internal sealed class VoucherRenewalItemConfiguration : IEntityTypeConfiguration<VoucherRenewalItem>
{
    public void Configure(EntityTypeBuilder<VoucherRenewalItem> builder)
    {
        builder.ToTable("voucher_renewal_items");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(e => e.SourceVoucherId)
            .HasColumnName("source_voucher_id")
            .IsRequired();

        builder.Property(e => e.TermCode)
            .HasColumnName("term_code")
            .HasMaxLength(8)
            .IsRequired();

        // Bare done-marker column, not a navigation: it may point at the source voucher (extend
        // branch) or a fresh stock voucher (replace branch). No FK — the SourceVoucher FK already
        // pins the row to fuel_vouchers, and this stays a nullable audit field.
        builder.Property(e => e.FulfilledVoucherId)
            .HasColumnName("fulfilled_voucher_id");

        builder.Property(e => e.FulfilledAtUtc)
            .HasColumnName("fulfilled_at_utc");

        builder.Property(e => e.AmountPaid)
            .HasColumnName("amount_paid")
            .HasColumnType("numeric(18,2)");

        builder.Property(e => e.PreviousCustomerExpiration)
            .HasColumnName("previous_customer_expiration");

        builder.Property(e => e.NewCustomerExpiration)
            .HasColumnName("new_customer_expiration");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasOne(e => e.Order)
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.SourceVoucher)
            .WithMany()
            .HasForeignKey(e => e.SourceVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.OrderId);
        builder.HasIndex(e => e.SourceVoucherId);
    }
}

