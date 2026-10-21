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
            .IsRequired().HasComment("The import that brought this stock in.");

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
               .IsRequired().HasComment("The fuel this batch is.");

        builder.Property(e => e.Brand)
            .HasColumnName("brand")
            .HasMaxLength(50)
            .IsRequired().HasComment("The brand's NAME - what a report groups by and what a parser reads. The id below is what" +
                    "the database can check, and the two are the same fact written twice.");

        // The key the brand text above is a copy of. Required, because the composite foreign key
        // below is what refuses a row whose brand is not its fuel's brand - and a foreign key with a
        // NULL in it is not compared at all, so a nullable key would be a key the database does not
        // check.
        builder.Property(e => e.BrandId)
            .HasColumnName("brand_id")
            .HasColumnType("uuid")
            .IsRequired().HasComment("The brand's id - the key the text above is a copy of. The composite foreign key" +
                    "(fuel_type_id, brand_id) -> fuel_types(id, brand_id) is what refuses a row whose brand is" +
                    "not its fuel's brand.");

        builder.Property(e => e.SupplierId)
            .HasColumnName("supplier_id")
            .IsRequired().HasComment("The company this stock was bought from. Unlike the brand, this is the party we settle with" +
                    "and hand vouchers back to.");

        builder.HasOne(e => e.Supplier)
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.EnteredByUserId)
            .HasColumnName("entered_by_user_id").HasComment("The user who entered the batch. NULL for a batch that came in through an import.");
        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired().HasComment("When the row was written.");

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired().HasComment("When it was last changed.");

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
        //
        // The key is the PAIR (fuel type, brand), not the fuel alone - which is what D-21's second
        // slice needs. A foreign key over one column says "this fuel exists"; a foreign key over two
        // says "this fuel exists AND this batch's brand is that fuel's brand", and the second half
        // is the fact the brand text on this row is a copy of. The pair points at the fuel's own
        // alternate key (id, brand_id), so the key spans one hop instead of two.
        //
        // Both halves of the pair are NOT NULL, which is what makes the key a key: a foreign key
        // with a NULL in it is not compared at all, so a nullable brand_id would be a brand the
        // database never checks.
        builder.HasOne<FuelTypeEntity>()
            .WithMany()
            .HasForeignKey(e => new { e.FuelTypeId, e.BrandId })
            .HasPrincipalKey(e => new { e.Id, e.BrandId })
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
