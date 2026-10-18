using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Configurations;

internal sealed class PurchaseBatchConfiguration : IEntityTypeConfiguration<PurchaseBatch>
{
    public void Configure(EntityTypeBuilder<PurchaseBatch> builder)
    {
        builder.ToTable("purchase_batches");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.ImportJobId)
            .HasColumnName("import_job_id")
            .IsRequired();

        builder.HasOne<VoucherImport>()
            .WithMany()
            .HasForeignKey(e => e.ImportJobId)
            .OnDelete(DeleteBehavior.Restrict);

        // Fuel identity is a free-text natural key everywhere in the catalog (fuel_types.id is
        // text), so this column stays text - matching the PK it points at is what makes the FK
        // expressible at all. A varchar(100)/text mismatch would have made it impossible.
        builder.Property(e => e.FuelTypeId)
            .HasColumnName("fuel_type_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Brand)
            .HasColumnName("brand")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.SupplierId)
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.HasOne(e => e.Supplier)
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.EnteredByUserId)
            .HasColumnName("entered_by_user_id");
        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // One row per (import × fuel). Upserts key on this pair.
        builder.HasIndex(e => new { e.ImportJobId, e.FuelTypeId })
            .IsUnique();

        // Supplier reconciliation lists a supplier's batches. EnteredByUserId is indexed by the
        // FK convention above.
        builder.HasIndex(e => e.SupplierId);

        // fuel_type_id is a catalog natural key, bounded by CHECK - see BrandConfiguration.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_purchase_batches_fuel_type_id_length", "length(fuel_type_id) <= 100");
        });
    }
}
