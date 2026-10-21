using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired().HasComment("The supplier's display name. Unique among suppliers - two rows with the same name are one" +
                    "counterparty recorded twice.");

        builder.Property(e => e.LegalForm)
            .HasColumnName("legal_form")
            .HasMaxLength(32).HasComment("ФОП / ТОВ / ТзОВ / ФОП-ЄЗ / інше. Which form it is decides which requisites are" +
                    "meaningful.");

        builder.Property(e => e.Phone)
            .HasColumnName("phone")
            .HasMaxLength(32).HasComment("The phone to call about this supplier.");

        builder.Property(e => e.Email)
            .HasColumnName("email")
            .HasMaxLength(200).HasComment("The address to write to.");

        // Single column because ФОП has an ІПН and an LLC has an ЄДРОПУ; they never both apply, and a
        // caller reading a supplier needs one field regardless of form.
        builder.Property(e => e.EdrIpn)
            .HasColumnName("edr_ipn")
            .HasMaxLength(20).HasComment("The registry code - ЄДРОПУ for a company, ІПН for an individual. Optional, and unique" +
                    "when it is there: two rows with the same code are the same counterparty recorded twice, and" +
                    "a business that pays two suppliers who are one person pays twice and reconciles the" +
                    "difference by hand.");

        // One supplier, one registry code. Two rows with the same code are the same counterparty
        // recorded twice, and a business that pays "two" suppliers who is one person pays twice
        // and reconciles the difference by hand. A supplier with no code cannot collide, so the
        // uniqueness costs nothing for one who has not been identified yet.
        builder.HasIndex(e => e.EdrIpn)
            .IsUnique();

        // A code that is there has to be a code. A blank is not a registry code, and the API
        // stores a blank as NULL rather than as "" - so this only ever sees a real value.
        builder.ToTable(t =>
            t.HasCheckConstraint("ck_suppliers_edr_ipn_format",
                "\"edr_ipn\" IS NULL OR \"edr_ipn\" ~ '^[0-9]{8,10}$'"));

        builder.Property(e => e.Rnkrr)
            .HasColumnName("rnkrr")
            .HasMaxLength(20).HasComment("РНОКР - the tax register number. Individuals have one too; companies may omit it.");

        builder.Property(e => e.Address)
            .HasColumnName("address")
            .HasMaxLength(500).HasComment("The supplier's address.");

        builder.Property(e => e.Notes)
            .HasColumnName("notes")
            .HasMaxLength(2000).HasComment("Free-form: settlement terms, who to call, what the invoice must include.");

        builder.Property(e => e.IsActive)
            .HasColumnName("is_active")
            .IsRequired().HasComment("Inactive suppliers stay on past vouchers for audit, but they are not offered to the" +
                    "operator.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired().HasComment("When the row was written.");

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired().HasComment("When it was last changed.");

        // HasDefaultValue as well as the CLR default: the value has to reach the database for the
        // supplier-creation paths that do not think in terms of routes at all, and EF omits an unset
        // column from the INSERT.
        builder.Property(e => e.AcquisitionSource)
            .HasColumnName("acquisition_source")
            .HasMaxLength(10)
            .HasDefaultValue(AcquisitionSources.Default)
            .IsRequired().HasComment("Which supply route this supplier sells on: direct, or through a partner. Immutable - a" +
                    "supplier that moves between routes is a different supply relationship, and a voucher bought" +
                    "last quarter has to keep reporting in the bucket it was bought into.");

        // The value set AcquisitionSources.All documents - see AcquisitionSources.
        builder.ToTable(t =>
            t.HasCheckConstraint("ck_suppliers_acquisition_source", "acquisition_source IN ('direct', 'partner')"));

        // The operator picks a supplier by name; duplicate names would make that ambiguous.
        builder.HasIndex(e => e.Name)
            .IsUnique();

        builder.HasIndex(e => e.IsActive);
    }
}