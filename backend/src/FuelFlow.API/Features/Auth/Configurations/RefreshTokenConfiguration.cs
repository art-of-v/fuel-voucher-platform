using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Auth.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired()
            .HasComment(                "The account the token belongs to.");

        builder.Property(e => e.FamilyId)
            .HasColumnName("family_id")
            .IsRequired()
            .HasComment(                "The family the token belongs to. Every token issued from one login shares a family, and" +
                    "a reuse of a revoked token revokes the whole family - that is how a stolen token is" +
                    "detected rather than merely used once.");

        builder.Property(e => e.DeviceId)
            .HasColumnName("device_id")
            .HasMaxLength(200)
            .HasComment(                "The device the token was issued to, when it is known.");

        builder.Property(e => e.RoleNameAtIssue)
            .HasColumnName("role_name_at_issue")
            .HasMaxLength(50)
            .HasComment(                "The role the account had WHEN the token was issued. The role is copied rather than read" +
                    "at use time, so that a role change does not silently change what an old token can do.");

        builder.Property(e => e.Purpose)
            .HasColumnName("purpose")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired()
            .HasDefaultValue(RefreshTokenPurpose.Session)
            .HasComment(                "What the token is for. Session = the client keeps and rotates it. DeviceBinding = " +
                    "the throwaway a native sign-in discards once the signed challenge mints the device-bound token; " +
                    "completing that challenge revokes it, and it never outlives the binding window.");

        builder.Property(e => e.Token)
            .HasColumnName("token")
            .HasMaxLength(200)
            .IsRequired()
            .HasComment(                "The token itself, hashed. The plaintext exists only in the response that issued it.");

        builder.Property(e => e.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .IsRequired()
            .HasComment(                "When the token stops being valid.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasComment(                "When it was issued.");

        builder.Property(e => e.IsRevoked)
            .HasColumnName("is_revoked")
            .IsRequired()
            .HasComment(                "Whether the token has been taken back.");

        builder.Property(e => e.RevokedAtUtc)
            .HasColumnName("revoked_at_utc")
            .HasComment(                "When it was taken back, or NULL while it has not been.");

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.Token)
            .IsUnique();

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => new { e.UserId, e.IsRevoked });
        builder.HasIndex(e => e.FamilyId);
        builder.HasIndex(e => new { e.UserId, e.DeviceId });
    }
}
