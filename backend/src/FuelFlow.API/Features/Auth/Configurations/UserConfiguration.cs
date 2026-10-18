using FuelFlow.Features.Orders.SharedModels;
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

        builder.Property(e => e.SettlementCode)
            .HasColumnName("settlement_code")
            .HasMaxLength(8)
            .IsRequired()
            // Store-generated. Without this EF includes the column in every INSERT and writes the
            // CLR default, so trg_users_assign_settlement_code never sees NULL and every row gets
            // the same code. See User.SettlementCode.
            .ValueGeneratedOnAdd();

        // Unique across every row, soft-deleted ones included - the opposite of phone_number and
        // referral_code, which a soft-deleted user is deliberately allowed to release. A settlement
        // code must never be reissued: payments recorded against a deleted account still have to
        // resolve, and a recycled code would silently re-point them at whoever registers next. The
        // old ones are small enough to keep, so the ledger stays readable.
        builder.HasIndex(e => e.SettlementCode)
            .IsUnique();

        builder.Property(e => e.BonusBalance)
               .HasColumnName("bonus_balance")
               .HasColumnType("numeric(12,2)")
               .HasDefaultValue(0m)
               .IsRequired();

        // A negative bonus is not a discount, it is a bug: nothing in the domain credits a balance
        // below zero, and one that can go negative silently means every figure derived from it - the
        // admin list, the user's own profile - is untrustworthy.
        builder.ToTable(t => t.HasCheckConstraint("ck_users_bonus_balance_non_negative", "bonus_balance >= 0"));

        builder.Property(e => e.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(e => e.TokenVersion)
            .HasColumnName("token_version")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(e => e.IsBanned)
            .HasColumnName("is_banned")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(e => e.IsQaAccount)
            .HasColumnName("is_qa_account")
            .HasDefaultValue(false)
            .IsRequired();

        // Arrived in PascalCase (and unbounded text) because the mapping was forgotten. Named and
        // bounded explicitly now: the address matches users.email, and the token hash is a
        // SHA-256 hex digest, never the token itself.
        builder.Property(e => e.PendingEmail)
            .HasColumnName("pending_email")
            .HasMaxLength(200);

        builder.Property(e => e.PendingEmailTokenHash)
            .HasColumnName("pending_email_token_hash")
            .HasMaxLength(100);

        builder.Property(e => e.PendingEmailExpiresAtUtc)
            .HasColumnName("pending_email_expires_at_utc");

        builder.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.SetNull);

        // orders.user_id carried no FK at all despite being NOT NULL, indexed and filtered on by
        // essentially every order query. Restrict also means a hard DELETE FROM users now fails
        // loudly instead of leaving orders - and the fulfillments and refunds behind them - as
        // rows that still count in revenue reports but name nobody.
        // contracts.user_id and user_contracts.user_id are declared from their own side.
        builder.HasMany<Order>()
            .WithOne()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(e => !e.IsDeleted);

        builder.HasIndex(e => e.PhoneNumber)
            .IsUnique()
            .HasFilter("is_deleted = false");

        // A deleted account must not squat its referral code forever: the filter mirrors
        // phone_number's, so a soft-deleted user releases the code for reuse.
        builder.HasIndex(e => e.ReferralCode)
            .IsUnique()
            .HasFilter("referral_code IS NOT NULL AND is_deleted = false");

        // D-02 additive step 1. Nullable, and nothing reads it yet - see User.PartyId. Not unique:
        // the 1:1 with a party is what the trigger guarantees, and a unique index here would also
        // forbid the two NULLs that a partially-migrated table legitimately has.
        builder.Property(e => e.PartyId)
            .HasColumnName("party_id");

        builder.HasIndex(e => e.PartyId);
    }
}
