using FuelFlow.Features.Ledger.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Ledger.Configurations;

internal sealed class OperatorLegalEntityConfiguration : IEntityTypeConfiguration<OperatorLegalEntity>
{
    public void Configure(EntityTypeBuilder<OperatorLegalEntity> builder)
    {
        builder.ToTable("operator_legal_entities");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Edrpou)
            .HasColumnName("edrpou")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.LegalForm)
            .HasColumnName("legal_form")
            .HasMaxLength(32);

        builder.Property(e => e.VatNumber)
            .HasColumnName("vat_number")
            .HasMaxLength(20);

        builder.Property(e => e.Rnkrr)
            .HasColumnName("rnkrr")
            .HasMaxLength(20);

        builder.Property(e => e.TaxRegime)
            .HasColumnName("tax_regime")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.RegisteredAddress)
            .HasColumnName("registered_address")
            .HasMaxLength(500);

        builder.Property(e => e.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.HasIndex(e => e.Edrpou).IsUnique();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_operator_legal_entities_tax_regime", "tax_regime IN ('Vat','Simplified')");
            // The identifier shapes are what a fiscal document has to print, so they are checked at
            // the storage layer rather than only by the form that typed them.
            t.HasCheckConstraint("ck_operator_legal_entities_edrpou_digits", "edrpou ~ '^[0-9]{8,10}$'");
            t.HasCheckConstraint("ck_operator_legal_entities_vat_number_digits", "vat_number IS NULL OR vat_number ~ '^[0-9]{12}$'");
            t.HasCheckConstraint("ck_operator_legal_entities_rnkrr_digits", "rnkrr IS NULL OR rnkrr ~ '^[0-9]{5}$'");
            t.HasCheckConstraint("ck_operator_legal_entities_name_not_blank", "btrim(name) <> ''");
            t.HasCheckConstraint("ck_operator_legal_entities_edrpou_not_blank", "btrim(edrpou) <> ''");
        });

        // Single-operator is the decision (see the type's remarks), so it is an invariant rather
        // than a comment: at most one row may be active, because two active rows would give a fiscal
        // document two possible issuers.
        //
        // A partial unique index, not a CHECK. A CHECK cannot read other rows - PostgreSQL rejects
        // the subquery outright - and this invariant is about the state of the table, not of the
        // row being written. Every row in this index has is_active = true, so uniqueness permits
        // exactly one of them.
        builder.HasIndex(e => e.IsActive)
            .IsUnique()
            .HasFilter("is_active")
            .HasDatabaseName("ux_operator_legal_entities_single_active");
    }
}
