using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FuelFlow.Features.Ledger.SharedModels;

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
            .IsRequired()
            .HasComment(                "The customer's phone number, and their identity: the login is the number.");

        builder.Property(e => e.RoleId)
            .HasColumnName("role_id")
            .HasComment(                "The role this account has, or NULL for an account with no role - which is a state the" +
                    "admin can repair, not a state the database should refuse.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasComment(                "When the account was created.");

        builder.Property(e => e.LastLoginAtUtc)
            .HasColumnName("last_login_at_utc")
            .HasComment(                "When it last signed in. NULL until it does.");

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired()
            .HasComment(                "When it last changed.");

        builder.Property(e => e.Email)
            .HasColumnName("email")
            .HasMaxLength(200)
            .HasComment(                "The customer's email, when they gave one.");

        builder.Property(e => e.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(100)
            .HasComment(                "The customer's first name.");

        builder.Property(e => e.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(100)
            .HasComment(                "The customer's last name.");

        builder.Property(e => e.Birthdate)
            .HasColumnName("birthdate")
            .HasComment(                "The customer's date of birth, when they gave it.");

        builder.Property(e => e.ProfileImageUrl)
            .HasColumnName("profile_image_url")
            .HasMaxLength(500)
            .HasComment(                "Where the avatar is.");

        builder.Property(e => e.ReferralCode)
            .HasColumnName("referral_code")
            .HasMaxLength(50)
            .HasComment(                "The code the customer chose to be referred by. Optional, and it may change - correct for" +
                    "marketing, and deliberately NOT the same thing as the settlement code.");

        builder.Property(e => e.ReferredBy)
            .HasColumnName("referred_by")
            .HasMaxLength(50)
            .HasComment(                "Who referred this customer, in their own words.");

        builder.Property(e => e.SettlementCode)
            .HasColumnName("settlement_code")
            .HasMaxLength(8)
            // NOT store-enforced in the model on purpose. The column IS NOT NULL in Postgres - the
            // migration says so and `The_column_is_not_null_in_the_database` asserts it - but the
            // value is store-generated, so from the application's point of view the property is
            // nullable: null means "not yet issued", which is true for the whole window between
            // `new User()` and the INSERT.
            //
            // Marking it required here would make EF's InMemory provider enforce NOT NULL on the
            // client, where there is no trigger to fill it. Every unit test that seeds a user would
            // then fail on a column the application never sets - which is the InMemory provider
            // asserting a database invariant it cannot possibly know about, not a real defect.
            .ValueGeneratedOnAdd()
            .HasComment(                "The short identifier an operator quotes to the finance channel to attribute money to" +
                    "this customer. Issued once by the database and immutable for the life of the account: a" +
                    "changed code orphans every payment recorded against it. Deliberately NOT the referral" +
                    "code - that one is optional and may change, and a customer without one would have no way" +
                    "to be paid on this route at all. It also avoids the 0/O/1/I/L pairs: it is transcribed" +
                    "by hand, and those are the pairs people misread. A mistyped code credits money to the" +
                    "wrong person, so the alphabet trades a little space for legibility. Staff-only, and it" +
                    "must not appear in any customer-facing response.");

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
               .IsRequired().HasComment("The bonus balance in UAH - the money the platform owes the customer. A balance, not a price:" +
                       "it is what is LEFT, and it can be negative when a company has used more than it has paid.");

        // A negative bonus is not a discount, it is a bug: nothing in the domain credits a balance
        // below zero, and one that can go negative silently means every figure derived from it - the
        // admin list, the user's own profile - is untrustworthy.
        builder.ToTable(t => t.HasCheckConstraint("ck_users_bonus_balance_non_negative", "bonus_balance >= 0"));

        builder.Property(e => e.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(false)
            .IsRequired().HasComment("Whether the account may sign in. Separate from being banned: an inactive account is one the" +
                    "customer closed, a banned one is one we closed.");

        builder.Property(e => e.TokenVersion)
            .HasColumnName("token_version")
            .HasDefaultValue(1)
            .IsRequired().HasComment("The version of the tokens this account's sessions carry. Bumping it invalidates every" +
                    "refresh token at once - which is what a password change or a ban has to do, and what no" +
                    "single-token revocation can do.");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired().HasComment("Soft delete. The user stays in the schema, because the orders, the money and the vouchers" +
                    "behind them have to stay readable.");

        builder.Property(e => e.IsBanned)
            .HasColumnName("is_banned")
            .HasDefaultValue(false)
            .IsRequired().HasComment("Whether the account is banned. Separate from being deleted: a banned account keeps its" +
                    "history and cannot sign in.");

        builder.Property(e => e.IsQaAccount)
            .HasColumnName("is_qa_account")
            .HasDefaultValue(false)
            .IsRequired().HasComment("Whether this is a test account. The QA identity is explicitly distinguishable from a real" +
                    "customer, and the reports that count customers leave it out.");

        // Arrived in PascalCase (and unbounded text) because the mapping was forgotten. Named and
        // bounded explicitly now: the address matches users.email, and the token hash is a
        // SHA-256 hex digest, never the token itself.
        builder.Property(e => e.PendingEmail)
            .HasColumnName("pending_email")
            .HasMaxLength(200).HasComment("The address waiting to be confirmed, while the current one still works. NULL when nothing" +
                    "is pending.");

        builder.Property(e => e.PendingEmailTokenHash)
            .HasColumnName("pending_email_token_hash")
            .HasMaxLength(100).HasComment("The HASH of the confirmation token, never the token itself: a database that is read by" +
                    "somebody must not be able to confirm an address.");

        builder.Property(e => e.PendingEmailExpiresAtUtc)
            .HasColumnName("pending_email_expires_at_utc").HasComment("When the pending address stops being claimable. The token is short-lived on purpose - a" +
                    "confirmation link that works for a year is a key.");

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
            .HasColumnName("party_id")
            .HasComment(                "The user's party record (D-02, additive step 1). NULLABLE on purpose: the documented" +
                    "sequence is insert, switch, reconcile, then retire, and this column is the insert." +
                    "Nothing reads it yet and no handler depends on it, so a user existing without one is not" +
                    "an error today - it becomes one when the column is promoted to NOT NULL, and that is a" +
                    "separate migration with a test that says so.");

        // Postgres does not index a foreign key column on its own, and the parent's delete check and
        // every join down this column scan without one.
        builder.HasIndex(e => e.PartyId);

        // --- F-14: the foreign key this column was missing -------------------------------------------------
        // The reference is real and single-parent, so the database enforces it. ON DELETE is stated
        // explicitly rather than left to the default: a schema that says NO ACTION by accident is a
        // schema nobody has thought about.
        builder.HasOne<Party>()
            .WithMany()
            .HasForeignKey(e => e.PartyId)
            .OnDelete(DeleteBehavior.Restrict);

        // The admin's customers list sorts by created_at_utc DESC, id - and no index on this table
        // led with that column, so the sort was a scan of the whole table. The list is a page now,
        // and a paged list still has to SORT: the page is the last fifty of the whole table.
        //
        // The migration creates it CONCURRENTLY, so the build does not take the ACCESS EXCLUSIVE
        // lock that would block the API for the length of it.
        builder.HasIndex(e => new { e.CreatedAtUtc, e.Id })
            .IsDescending(true, false)
            .HasDatabaseName("ix_users_created_at_utc_id");
    }
}
