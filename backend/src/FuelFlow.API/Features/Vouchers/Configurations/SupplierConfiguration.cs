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

        // The operator picks a supplier by name; duplicate names would make that ambiguous.
        builder.HasIndex(e => e.Name)
            .IsUnique();

        builder.HasIndex(e => e.IsActive);
    }
}