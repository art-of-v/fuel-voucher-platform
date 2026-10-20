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

        // --- F-23: a reference column without an index ----------------------------------------------
        // the batch names the operator who entered it. No foreign key here on purpose - this is an audit row, and an audit row must not
        // be the thing that blocks deleting its own subject - but the reference is still read, and
        // without an index every read of it is a scan.
        builder.HasIndex(e => e.EnteredByUserId);
    }
}
