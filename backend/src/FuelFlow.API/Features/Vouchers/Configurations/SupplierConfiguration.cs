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
            .IsRequired();

        builder.Property(e => e.LegalForm)
            .HasColumnName("legal_form")
            .HasMaxLength(32);

        builder.Property(e => e.Phone)
            .HasColumnName("phone")
            .HasMaxLength(32);

        builder.Property(e => e.Email)
            .HasColumnName("email")
            .HasMaxLength(200);

        // Single column because ФОП has an ІПН and an LLC has an ЄДРОПУ; they never both apply, and a
        // caller reading a supplier needs one field regardless of form.
        builder.Property(e => e.EdrIpn)
            .HasColumnName("edr_ipn")
            .HasMaxLength(20);

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
            .HasMaxLength(20);

        builder.Property(e => e.Address)
            .HasColumnName("address")
            .HasMaxLength(500);

        builder.Property(e => e.Notes)
            .HasColumnName("notes")
            .HasMaxLength(2000);

        builder.Property(e => e.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // HasDefaultValue as well as the CLR default: the value has to reach the database for the
        // supplier-creation paths that do not think in terms of routes at all, and EF omits an unset
        // column from the INSERT.
        builder.Property(e => e.AcquisitionSource)
            .HasColumnName("acquisition_source")
            .HasMaxLength(10)
            .HasDefaultValue(AcquisitionSources.Default)
            .IsRequired();

        // The value set AcquisitionSources.All documents - see AcquisitionSources.
        builder.ToTable(t =>
            t.HasCheckConstraint("ck_suppliers_acquisition_source", "acquisition_source IN ('direct', 'partner')"));

        // The operator picks a supplier by name; duplicate names would make that ambiguous.
        builder.HasIndex(e => e.Name)
            .IsUnique();

        builder.HasIndex(e => e.IsActive);
    }
}