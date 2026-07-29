using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Auth.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id);

        builder.Property(e => e.PhoneNumber)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.RoleId);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        builder.Property(e => e.LastLoginAtUtc);

        builder.Property(e => e.UpdatedAtUtc)
            .IsRequired();

        builder.Property(e => e.Email)
            .HasMaxLength(200);

        builder.Property(e => e.FirstName)
            .HasMaxLength(100);

        builder.Property(e => e.LastName)
            .HasMaxLength(100);

        builder.Property(e => e.Birthdate);

        builder.Property(e => e.ProfileImageUrl)
            .HasMaxLength(500);

        builder.Property(e => e.ReferralCode)
            .HasMaxLength(50);

        builder.Property(e => e.ReferredBy)
            .HasMaxLength(50);

        builder.Property(e => e.BonusBalance)
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.PhoneNumber)
            .IsUnique();

        builder.HasIndex(e => e.ReferralCode)
            .IsUnique()
            .HasFilter("referral_code IS NOT NULL");
    }
}
