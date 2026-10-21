using FuelFlow.Features.Contracts.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Contracts.Configurations;

internal sealed class ContractVersionConfiguration : IEntityTypeConfiguration<ContractVersion>
{
    public void Configure(EntityTypeBuilder<ContractVersion> builder)
    {
        builder.ToTable("contract_versions");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.BrandId)
            .HasColumnName("brand_id")
            .IsRequired()
            .HasComment("The provider whose text this is. One text per provider - the terms of one network, not of the platform.");

        builder.Property(e => e.Version)
            .HasColumnName("version")
            .IsRequired()
            .HasComment("The version number, counting up from 1 within the provider. A new version never overwrites the old one - what a customer signed is a question the audit trail has to answer.");

        builder.Property(e => e.Body)
            .HasColumnName("body")
            .IsRequired()
            .HasComment("The text itself - what the customer reads before signing. This is the whole point of the row: before it, the screen's REVIEW THE TEXT had nothing behind it.");

        builder.Property(e => e.TermDays)
            .HasColumnName("term_days")
            .IsRequired()
            .HasComment("How long a contract made against this version is valid, in days. The term belongs to the text: a new version of the terms is a different agreement with a different term.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasComment("When this version was published.");

        builder.Property(e => e.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .HasComment("Who published it. NULL for a version that was seeded rather than written by somebody.");

        // One current text per provider, and the versions of a provider are countable. The
        // uniqueness is what makes "the next version" a well-defined number: two versions with
        // the same number would leave the contract's pointer ambiguous.
        builder.HasIndex(e => new { e.BrandId, e.Version }).IsUnique();

        // The writer reads the current version of every provider, and it reads it by brand.
        builder.HasIndex(e => e.BrandId);

        builder.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FuelFlow.SharedKernel.Domain.User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            // A version number of zero is not a version, and a term of zero days is a contract
            // that expires the moment it is made.
            t.HasCheckConstraint("ck_contract_versions_version_positive", "version > 0");
            t.HasCheckConstraint("ck_contract_versions_term_positive", "term_days > 0");

            // An empty text is not a text. The customer is asked to read it, and a blank page
            // is not something a signature can be given to.
            t.HasCheckConstraint("ck_contract_versions_body_not_blank", "btrim(body) <> ''");
        });
    }
}
