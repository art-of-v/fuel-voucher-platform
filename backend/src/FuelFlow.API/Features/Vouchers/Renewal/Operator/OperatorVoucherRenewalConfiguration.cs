using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Renewal.Operator;

internal sealed class OperatorVoucherRenewalConfiguration : IEntityTypeConfiguration<OperatorVoucherRenewal>
{
    public void Configure(EntityTypeBuilder<OperatorVoucherRenewal> builder)
    {
        builder.ToTable("operator_voucher_renewals");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.VoucherId)
            .HasColumnName("voucher_id")
            .IsRequired();

        // Bare audit link, not a navigation: the stock voucher on the replace branch, null on extend.
        // No FK — the VoucherId FK already pins the row to fuel_vouchers.
        builder.Property(e => e.ReplacementVoucherId)
            .HasColumnName("replacement_voucher_id");

        builder.Property(e => e.CustomerUserId)
            .HasColumnName("customer_user_id")
            .IsRequired();

        builder.Property(e => e.Branch)
            .HasColumnName("branch")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.TermCode)
            .HasColumnName("term_code")
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(e => e.OldExpiration)
            .HasColumnName("old_expiration")
            .IsRequired();

        builder.Property(e => e.NewExpiration)
            .HasColumnName("new_expiration")
            .IsRequired();

        builder.Property(e => e.SurchargeUah)
            .HasColumnName("surcharge_uah")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

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
            .IsRequired();

        builder.HasOne(e => e.Voucher)
            .WithMany()
            .HasForeignKey(e => e.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.VoucherId);
        builder.HasIndex(e => e.CustomerUserId);
        builder.HasIndex(e => e.CreatedAtUtc);
    }
}
