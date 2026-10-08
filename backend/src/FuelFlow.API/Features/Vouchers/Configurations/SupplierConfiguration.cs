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

        builder.Property(e => e.ContactInfo)
            .HasColumnName("contact_info")
            .HasMaxLength(500);

        // Plain text, no FK: mirrors stations.id, which is a text key rather than a surrogate. Not unique —
        // one supplier may deal in several brands.
        builder.Property(e => e.StationId)
            .HasColumnName("station_id")
            .HasMaxLength(50);

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

        // The operator picks a supplier by name; duplicate active names would make that ambiguous.
        builder.HasIndex(e => e.Name)
            .IsUnique();

        // The voucher list filters by supplier.
        builder.HasIndex(e => e.IsActive);
    }
}