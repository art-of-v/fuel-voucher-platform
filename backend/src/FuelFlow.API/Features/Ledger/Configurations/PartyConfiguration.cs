using FuelFlow.Features.Ledger.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Ledger.Configurations;

internal sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("parties");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.PartyType)
            .HasColumnName("party_type")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_parties_party_type", "party_type IN ('Natural','Legal')");
            t.HasCheckConstraint("ck_parties_display_name_not_blank", "btrim(display_name) <> ''");
        });
    }
}

internal sealed class NaturalPersonConfiguration : IEntityTypeConfiguration<NaturalPerson>
{
    public void Configure(EntityTypeBuilder<NaturalPerson> builder)
    {
        builder.ToTable("natural_persons");

        // Keyed by the party it describes: a party is a party whichever subtype row exists, which is
        // what lets one foreign key in the journal address both kinds of counterparty.
        builder.HasKey(e => e.PartyId);

        builder.Property(e => e.PartyId)
            .HasColumnName("party_id");

        builder.Property(e => e.Birthdate)
            .HasColumnName("birthdate");

        builder.Property(e => e.TaxId)
            .HasColumnName("tax_id")
            .HasMaxLength(20);

        builder.HasOne(e => e.Party)
            .WithOne(e => e.NaturalPerson)
            .HasForeignKey<NaturalPerson>(e => e.PartyId)
            // RESTRICT: a party referenced by a journal line cannot be deleted out from under it.
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            // ІПН is 9 or 10 digits. Checked here because a malformed tax id on a fiscal document is
            // a rejected document, and the form that typed it is not the only thing that can be wrong.
            t.HasCheckConstraint("ck_natural_persons_tax_id_digits", "tax_id IS NULL OR tax_id ~ '^[0-9]{9,10}$'");
            t.HasCheckConstraint("ck_natural_persons_tax_id_length", "tax_id IS NULL OR length(tax_id) <= 20");
        });
    }
}