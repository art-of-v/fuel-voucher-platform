using FuelFlow.Persistence;
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
        // The fuel id is a uuid since planning D-21: a uuid column, so this can carry a real
        // foreign key one day - which is what D-02 is waiting for.
           builder.Property(e => e.FuelTypeId)
               .HasColumnName("fuel_type_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(e => e.Brand)
            .HasColumnName("brand")
            .HasMaxLength(50)
            .IsRequired();

        // The key the brand text above is a copy of. Nullable for now: the column arrives with the
        // backfill, and the rows that are written before the readers move to the id still carry
        // only the name. The foreign key over (fuel type, brand) comes with the same migration.
        builder.Property(e => e.BrandId)
            .HasColumnName("brand_id")
            .HasColumnType("uuid");

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

        // Supplier reconciliation lists a supplier's batches.
        builder.HasIndex(e => e.SupplierId);

        // fuel_type_id is a uuid since planning D-21's second slice, and a uuid has no length to
        // bound - the check that bounded it as text went with the text.

        // --- F-14: the foreign key this column was missing -------------------------------------------------
        // The reference is real and single-parent, so the database enforces it. ON DELETE is stated
        // explicitly rather than left to the default: a schema that says NO ACTION by accident is a
        // schema nobody has thought about.
        builder.HasOne<FuelTypeEntity>()
            .WithMany()
            .HasForeignKey(e => e.FuelTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Postgres does not index a foreign key column on its own, and the parent's delete check and
        // every join down this column scan without one.
        builder.HasIndex(e => e.FuelTypeId);

        // entered_by_user_id carries no foreign key - this is an audit row, and an audit row must
        // not be the thing that blocks deleting its own subject - and no index either. It is
        // written and never read: no query filters a batch by the operator who entered it, and an
        // index there would be the exact defect F-11 removed - a write cost on every insert that
        // serves no query.
    }
}
