using FuelFlow.Features.Contracts.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Contracts.Configurations;

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("contracts");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired().HasComment("The user who signs this contract.");

        builder.Property(e => e.LegalEntityId)
            .HasColumnName("legal_entity_id")
            .IsRequired().HasComment("The company the contract is for.");

        builder.Property(e => e.BrandId)
            .HasColumnName("brand_id")
            .HasColumnType("uuid")
            .IsRequired().HasComment("The brand the contract covers - the terms of one brand, not of the platform.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired().HasComment("When the contract was created.");

        // The validity term, mandatory since 2026-10-20. A contract that never expires is a
        // record that something once happened, not an agreement.
        builder.Property(e => e.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .IsRequired().HasComment("When the contract stops being valid. Mandatory since 2026-10-20, on the business's" +
                    "instruction: a contract without a term is not a contract, it is a record that something once" +
                    "happened. The sign endpoint refuses to sign a contract that has already expired, so the" +
                    "term is not a field nobody reads - it is the thing that stops a signature.");

        // The version of the provider's text this contract is against. The customer reads this
        // version and signs it, so it is recorded rather than inferred from "whatever is
        // current" - which is a number that moves.
        //
        // The default of 1 is the backfill: a contract created before the versions existed was
        // against the only text there was, and saying "version 1" is true in the way a default
        // is true - and the writer always sets the version itself, so the default is only what a
        // writer that forgot produces.
        builder.Property(e => e.TermsVersion)
            .HasColumnName("terms_version")
            .HasDefaultValue((short)1)
            .IsRequired()
            .HasComment("The version of the provider's text this contract is against - the one the customer reads and signs. A contract that says 'version 2' while the provider's text is at version 3 is a question the act and the audit trail have to be able to answer.");

        // A term that ends before it starts is a typo, and the database is the cheapest place
        // to refuse it.
        builder.ToTable(t =>
            t.HasCheckConstraint("ck_contracts_expiry_after_creation",
                "expires_at_utc > created_at_utc"));

        // A version number of zero is not a version.
        builder.ToTable(t =>
            t.HasCheckConstraint("ck_contracts_terms_version_positive", "terms_version > 0"));

        // One contract per company per provider. The writer adds what is missing and changes
        // nothing else, and this index is what makes "missing" a fact the database can state:
        // a second contract for the same company and the same provider is a duplicate, not a
        // new document.
        builder.HasIndex(e => new { e.LegalEntityId, e.BrandId }).IsUnique();
        // A signed contract is not owned by the account that signed it, nor by the company or the
        // brand it names. Cascade here used to walk users -> legal_entities -> contracts ->
        // user_contracts and end at a digital signature; Restrict stops it at the first hop.
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Entity)
            .WithMany()
            .HasForeignKey(e => e.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.LegalEntityId);
        builder.HasIndex(e => e.BrandId);

        // "Which contracts are about to expire" is the question the term exists to answer, and
        // it is the question an admin asks first.
        builder.HasIndex(e => e.ExpiresAtUtc);
    }
}
