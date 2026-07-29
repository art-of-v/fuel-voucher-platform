using FuelFlow.Features.Auth.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Auth.Configurations;

internal sealed class VerificationCodeConfiguration : IEntityTypeConfiguration<VerificationCode>
{
    public void Configure(EntityTypeBuilder<VerificationCode> builder)
    {
        builder.ToTable("verification_codes");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id);

        builder.Property(e => e.PhoneNumber)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.Code)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(e => e.ExpiresAtUtc)
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        builder.Property(e => e.IsUsed)
            .IsRequired();

        builder.Property(e => e.UsedAtUtc);

        builder.HasIndex(e => e.PhoneNumber);
        builder.HasIndex(e => e.ExpiresAtUtc);
        builder.HasIndex(e => new { e.PhoneNumber, e.IsUsed, e.ExpiresAtUtc })
            .IsDescending(false, false, true);
    }
}
