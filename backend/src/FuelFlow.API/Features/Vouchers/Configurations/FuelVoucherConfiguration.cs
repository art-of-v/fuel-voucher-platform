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

        builder.Property(e => e.Id);

        builder.Property(e => e.Provider)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.FuelTypeId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Liters)
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(e => e.ExpirationDate)
            .IsRequired();

        builder.Property(e => e.VoucherNumber)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.QrPayload)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.FuelSubtype)
            .HasMaxLength(50);

        builder.Property(e => e.RedemptionRules)
            .HasColumnType("text");

        builder.Property(e => e.ImageUrl)
            .HasColumnType("text");

        builder.Property(e => e.ExternalId)
            .HasMaxLength(200);

        builder.Property(e => e.AssignedToUserId);

        builder.Property(e => e.ImportJobId);

        builder.Property(e => e.QrParametersId);

        builder.HasOne(e => e.QrParameters)
            .WithMany()
            .HasForeignKey(e => e.QrParametersId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.AssignedToUser)
            .WithMany()
            .HasForeignKey(e => e.AssignedToUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.ImportJob)
            .WithMany()
            .HasForeignKey(e => e.ImportJobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasQueryFilter(e => !e.IsDeleted);

        builder.Property(e => e.UpdatedAtUtc)
            .IsRequired();

        builder.Property(e => e.VerificationMismatchPercent)
            .HasColumnType("double precision");

        builder.Property(e => e.VerificationMismatchedModules);

        builder.Property(e => e.VerificationTotalModules);

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
        builder.HasIndex(e => new { e.AssignedToUserId, e.Status });
        builder.HasIndex(e => e.ExternalId)
            .IsUnique()
            .HasFilter("external_id IS NOT NULL");
    }
}
