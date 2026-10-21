using FuelFlow.Features.Contracts.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Contracts.Configurations;

internal sealed class LegalEntityConfiguration : IEntityTypeConfiguration<LegalEntity>
{
    public void Configure(EntityTypeBuilder<LegalEntity> builder)
    {
        builder.ToTable("legal_entities");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired().HasComment("The user who owns this company.");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired().HasComment("The company's name.");

        builder.Property(e => e.Edrpou)
            .HasColumnName("edrpou")
            .HasMaxLength(20)
            .IsRequired().HasComment("The EDDRPOU - the state registry number that identifies the company.");

        builder.Property(e => e.VatNumber)
            .HasColumnName("vat_number")
            .HasMaxLength(20).HasComment("The VAT number, if the company is a VAT payer. NULL is a real answer.");

        builder.Property(e => e.Address)
            .HasColumnName("address")
            .HasMaxLength(500).HasComment("The company's address.");

        builder.Property(e => e.DirectorName)
            .HasColumnName("director_name")
            .HasMaxLength(200).HasComment("The director's name, as the documents name them.");

        builder.Property(e => e.Phone)
            .HasColumnName("phone")
            .HasMaxLength(20).HasComment("The contact phone.");

        builder.Property(e => e.Email)
            .HasColumnName("email")
            .HasMaxLength(200).HasComment("The contact email.");

        // The default used to be '-infinity', and it was not chosen: it is what Npgsql writes for
        // DateTime.MinValue, which is what EF's convention put into the migrations that created this
        // column. -infinity sorts before every real timestamp and serialises badly, and it is a value
        // no application means.
        //
        // now() is the honest fallback for a raw INSERT that does not state the timestamp, and
        // ValueGeneratedNever keeps the application in charge: it still sends its own value on every
        // insert, exactly as it did before, so nothing about the application's behaviour changes. The
        // default is a safety net for SQL that does not come from EF.
        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasDefaultValueSql("now()")
            .ValueGeneratedNever()
            .IsRequired().HasComment("When the row was written.");

        // The default used to be '-infinity', and it was not chosen: it is what Npgsql writes for
        // DateTime.MinValue, which is what EF's convention put into the migrations that created this
        // column. -infinity sorts before every real timestamp and serialises badly, and it is a value
        // no application means.
        //
        // now() is the honest fallback for a raw INSERT that does not state the timestamp, and
        // ValueGeneratedNever keeps the application in charge: it still sends its own value on every
        // insert, exactly as it did before, so nothing about the application's behaviour changes. The
        // default is a safety net for SQL that does not come from EF.
        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasDefaultValueSql("now()")
            .ValueGeneratedNever()
            .IsRequired().HasComment("When it was last changed.");

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Multi-company (epic #103): a user may own several legal entities, so the
        // user_id index is a plain (non-unique) lookup index; only edrpou stays unique.
        builder.HasIndex(e => e.UserId);

        builder.HasIndex(e => e.Edrpou)
            .IsUnique();
    }
}

