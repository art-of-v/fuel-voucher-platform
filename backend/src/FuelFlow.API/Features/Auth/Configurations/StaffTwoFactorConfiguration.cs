using FuelFlow.Features.Auth.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Auth.Configurations;

internal sealed class StaffTwoFactorConfiguration : IEntityTypeConfiguration<StaffTwoFactor>
{
    public void Configure(EntityTypeBuilder<StaffTwoFactor> builder)
    {
        builder.ToTable("staff_two_factor");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired()
            .HasComment(                "The staff account the second factor belongs to. One row per account: a factor is bound to the " +
                    "person, not to the phone they happen to carry.");

        builder.Property(e => e.SecretCiphertext)
            .HasColumnName("secret_ciphertext")
            .HasComment(                "The TOTP secret, encrypted with the environment's AES-256 key. NULL while the account has no " +
                    "second factor yet - a secret that was handed out and never proven lives in the pending column instead.");

        builder.Property(e => e.PendingSecretCiphertext)
            .HasColumnName("pending_secret_ciphertext")
            .HasComment(                "A secret that was sent to a client and has not been proven yet, encrypted. The next sign-in attempt " +
                    "overwrites it, so an enrolment nobody finished never becomes a factor nobody chose.");

        builder.Property(e => e.PendingChallengeHash)
            .HasColumnName("pending_challenge_hash")
            .HasMaxLength(64)
            .HasComment(                "The hash of the outstanding challenge - the proof that the first factor was verified for this " +
                    "account and that a second factor may now be attempted. NULL when no challenge is outstanding.");

        builder.Property(e => e.PendingExpiresAtUtc)
            .HasColumnName("pending_expires_at_utc")
            .HasComment(                "When the outstanding challenge stops being redeemable.");

        builder.Property(e => e.FailedAttempts)
            .HasColumnName("failed_attempts")
            .HasDefaultValue(0)
            .IsRequired()
            .HasComment(                "How many times a code was tried against the outstanding challenge and failed. The challenge is " +
                    "burned once the limit is reached, so one challenge cannot be brute-forced.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasComment(                "When the row was created.");

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired()
            .HasComment(                "When the row last changed.");

        builder.Property(e => e.LastVerifiedAtUtc)
            .HasColumnName("last_verified_at_utc")
            .HasComment(                "When a code was last accepted for this account, or NULL while it never has been.");

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One factor per account. The database says so, not just the code that writes the row.
        builder.HasIndex(e => e.UserId)
            .IsUnique();

        // The challenge is looked up by its hash on every second-factor attempt.
        builder.HasIndex(e => e.PendingChallengeHash)
            .HasFilter("pending_challenge_hash IS NOT NULL");
    }
}
