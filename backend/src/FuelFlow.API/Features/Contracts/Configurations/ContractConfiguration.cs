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
            .IsRequired();

        builder.Property(e => e.LegalEntityId)
            .HasColumnName("legal_entity_id")
            .IsRequired();

        builder.Property(e => e.BrandId)
            .HasColumnName("brand_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        // The validity term, mandatory since 2026-10-20. A contract that never expires is a
        // record that something once happened, not an agreement.
        builder.Property(e => e.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .IsRequired();

        // A term that ends before it starts is a typo, and the database is the cheapest place
        // to refuse it.
        builder.ToTable(t =>
            t.HasCheckConstraint("ck_contracts_expiry_after_creation",
                "expires_at_utc > created_at_utc"));

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
