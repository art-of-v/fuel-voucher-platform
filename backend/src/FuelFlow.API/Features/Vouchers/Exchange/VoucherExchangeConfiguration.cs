using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Exchange;

internal sealed class VoucherExchangeConfiguration : IEntityTypeConfiguration<VoucherExchange>
{
    public void Configure(EntityTypeBuilder<VoucherExchange> builder)
    {
        builder.ToTable("voucher_exchanges");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.ExchangeBatchId)
            .HasColumnName("exchange_batch_id")
            .IsRequired();

        builder.Property(e => e.OldVoucherId)
            .HasColumnName("old_voucher_id")
            .IsRequired();

        // Bare audit link to the replacement voucher (also a fuel_vouchers row). No FK: it stays a
        // nullable marker — null when the old voucher was not paired. Mirrors VoucherRenewalItem.
        builder.Property(e => e.NewVoucherId)
            .HasColumnName("new_voucher_id");

        builder.Property(e => e.FuelTypeId)
            .HasColumnName("fuel_type_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Provider)
            .HasColumnName("provider")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.SurchargeUah)
            .HasColumnName("surcharge_uah")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(e => e.CostPerLiterApplied)
            .HasColumnName("cost_per_liter_applied")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.InvoiceNumber)
            .HasColumnName("invoice_number")
            .HasMaxLength(100);

        builder.Property(e => e.InvoiceDate)
            .HasColumnName("invoice_date");

        builder.Property(e => e.ActingUserId)
            .HasColumnName("acting_user_id")
            .IsRequired();

        builder.Property(e => e.ActingUserName)
            .HasColumnName("acting_user_name")
            .HasMaxLength(200);

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(e => e.OldVoucher)
            .WithMany()
            .HasForeignKey(e => e.OldVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.ExchangeBatchId);
        builder.HasIndex(e => e.OldVoucherId);
        builder.HasIndex(e => e.NewVoucherId);
    }
}
