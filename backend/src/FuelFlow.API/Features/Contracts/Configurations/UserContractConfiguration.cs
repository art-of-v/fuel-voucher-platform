using FuelFlow.Features.Contracts.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Contracts.Configurations;

internal sealed class UserContractConfiguration : IEntityTypeConfiguration<UserContract>
{
    public void Configure(EntityTypeBuilder<UserContract> builder)
    {
        builder.ToTable("user_contracts");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired().HasComment("The user who signed.");

        builder.Property(e => e.ContractId)
            .HasColumnName("contract_id")
            .IsRequired().HasComment("The contract that was signed.");

        builder.Property(e => e.SignatureData)
            .HasColumnName("signature_data").HasComment("The signature itself, as it was captured. NULL for a contract signed on paper and keyed in" +
                    "by hand - which is a real answer, not a missing value.");

        builder.Property(e => e.SignedAtUtc)
            .HasColumnName("signed_at_utc")
            .IsRequired().HasComment("When it was signed.");

        // The version of the text that was actually signed. The contract's own version is what
        // it points at NOW; this is what the customer read when they signed, and a new version
        // of the terms does not rewrite a signature.
        //
        // The default of 1 is the backfill for the signatures that were made before the
        // versions existed: they were against the only text there was.
        builder.Property(e => e.TermsVersion)
            .HasColumnName("terms_version")
            .HasDefaultValue((short)1)
            .IsRequired()
            .HasComment("The version of the text that was actually signed. The contract's own version is what it points at NOW; this is what the customer read at the moment they signed, and the two can differ.");

        builder.ToTable(t =>
            t.HasCheckConstraint("ck_user_contracts_terms_version_positive", "terms_version > 0"));

        // A signature outlives the account that gave it. Cascade here meant that deleting a user
        // erased every contract they had signed - which is exactly the history #217 keeps.
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Contract)
            .WithMany()
            .HasForeignKey(e => e.ContractId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.ContractId);
    }
}
