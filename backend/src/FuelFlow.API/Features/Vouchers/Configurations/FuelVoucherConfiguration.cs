using FuelFlow.Features.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Configurations;

internal sealed class FuelVoucherConfiguration : IEntityTypeConfiguration<FuelVoucher>
{
    public void Configure(EntityTypeBuilder<FuelVoucher> builder)
    {
        builder.ToTable("fuel_vouchers");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Provider)
            .HasColumnName("provider")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.FuelTypeId)
            .HasColumnName("fuel_type_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Liters)
            .HasColumnName("liters")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(e => e.ExpirationDate)
            .HasColumnName("expiration_date")
            .IsRequired();

        builder.Property(e => e.VoucherNumber)
            .HasColumnName("voucher_number")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.QrPayload)
            .HasColumnName("qr_payload")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.FuelSubtype)
            .HasColumnName("fuel_subtype")
            .HasMaxLength(50);

        builder.Property(e => e.RedemptionRules)
            .HasColumnName("redemption_rules")
            .HasColumnType("text");

        builder.Property(e => e.ImageUrl)
            .HasColumnName("image_url")
            .HasColumnType("text");

        builder.Property(e => e.AssignedToUserId)
            .HasColumnName("assigned_to_user_id")
            .HasMaxLength(100);

        builder.Property(e => e.ImportJobId)
            .HasColumnName("import_job_id");

        builder.Property(e => e.QrParametersId)
            .HasColumnName("qr_parameters_id");

        builder.HasOne(e => e.QrParameters)
            .WithMany()
            .HasForeignKey(e => e.QrParametersId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.Property(e => e.VerificationMismatchPercent)
            .HasColumnName("verification_mismatch_percent")
            .HasColumnType("double precision");

        builder.Property(e => e.VerificationMismatchedModules)
            .HasColumnName("verification_mismatched_modules");

        builder.Property(e => e.VerificationTotalModules)
            .HasColumnName("verification_total_modules");

        builder.HasIndex(e => e.VoucherNumber)
            .IsUnique();

        builder.HasIndex(e => e.QrPayload)
            .IsUnique();

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.ExpirationDate);
        builder.HasIndex(e => e.Provider);
        builder.HasIndex(e => e.FuelTypeId);
        builder.HasIndex(e => e.AssignedToUserId);
        builder.HasIndex(e => new { e.Provider, e.FuelTypeId, e.Liters, e.Status });
        builder.HasIndex(e => e.ImportJobId);
    }
}
