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

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.RoleId)
            .HasColumnName("role_id");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(e => e.LastLoginAtUtc)
            .HasColumnName("last_login_at_utc");

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.Property(e => e.Email)
            .HasColumnName("email")
            .HasMaxLength(200);

        builder.Property(e => e.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(100);

        builder.Property(e => e.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(100);

        builder.Property(e => e.Birthdate)
            .HasColumnName("birthdate");

        builder.Property(e => e.ProfileImageUrl)
            .HasColumnName("profile_image_url")
            .HasMaxLength(500);

        builder.Property(e => e.ReferralCode)
            .HasColumnName("referral_code")
            .HasMaxLength(50);

        builder.Property(e => e.ReferredBy)
            .HasColumnName("referred_by")
            .HasMaxLength(50);

        builder.Property(e => e.BonusBalance)
            .HasColumnName("bonus_balance")
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
