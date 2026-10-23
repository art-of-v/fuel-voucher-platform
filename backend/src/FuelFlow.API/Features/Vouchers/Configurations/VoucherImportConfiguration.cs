using FuelFlow.Features.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Configurations;

internal sealed class VoucherImportConfiguration : IEntityTypeConfiguration<VoucherImport>
{
    public void Configure(EntityTypeBuilder<VoucherImport> builder)
    {
        builder.ToTable("voucher_imports");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(500)
            .IsRequired().HasComment("The file as it arrived - the name the operator's machine gave it. What the operator" +
                    "recognises, not a key.");

        builder.Property(e => e.PageCount)
            .HasColumnName("page_count")
            .IsRequired().HasComment("How many pages the file had, as the importer counted them. The importer's own count, not the" +
                    "reader's.");

        builder.Property(e => e.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .IsRequired().HasComment("When the import began.");

        builder.Property(e => e.CompletedAtUtc)
            .HasColumnName("completed_at_utc").HasComment("When the import finished - successfully or not. NULL while it is still running, and the" +
                    "database ties this to the status below.");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired().HasComment("Started, Completed or Failed - the three values the importer writes. This status gates the" +
                    "whole intake, so it is pinned by a CHECK rather than left as free text.");

        builder.Property(e => e.ImportedCount)
            .HasColumnName("imported_count")
            .IsRequired().HasComment("How many vouchers the import actually created.");

        builder.Property(e => e.DuplicateCount)
            .HasColumnName("duplicate_count")
            .IsRequired().HasComment("How many rows were already there - the same voucher seen twice. Not an error: a supplier's" +
                    "file often overlaps the previous one.");

        builder.Property(e => e.FailedCount)
            .HasColumnName("failed_count")
            .IsRequired().HasComment("How many rows could not be read at all.");

        // The operator's declaration for the delivery, written once at upload and inherited by every
        // voucher in the batch. It is the first half of supplier/batch reconciliation, and the reason the
        // batch list can say who the paper is settled with even before a cost has been entered.
        //
        // Nullable only for rows written before the supplier picker existed: production had no imports at
        // that point (SUPPLIERS_AND_VOUCHER_COST), but a test or restored row may. A new import always sets
        // it, and the handler refuses an unknown supplier before parsing.
        //
        // It is NOT a provenance proof: the payload carries no supplier signature, serial or registry entry.
        builder.Property(e => e.SupplierId)
            .HasColumnName("supplier_id")
            .HasComment("The supplier the operator declared for this delivery. An operator assertion, not a " +
                    "cryptographic or registry verification - no such check exists in the payload.");
        builder.HasOne(e => e.Supplier)
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.SupplierId);

        builder.Property(e => e.DecodeIntegrityFailedCount)
            .HasColumnName("decode_integrity_failed_count")
            .IsRequired().HasComment("How many rows were read but failed the decode-integrity check: the printed QR matrix " +
                    "disagreed with the decoded payload by more than 2% of modules. Import QA, not a verdict on the " +
                    "supplier.");

        builder.Property(e => e.DecodeIntegrityWarningCount)
            .HasColumnName("decode_integrity_warning_count")
            .IsRequired().HasComment("How many rows passed the decode-integrity check with a 1-2% module mismatch - something " +
                    "odd in the image, not proof that the voucher is counterfeit or genuine.");

        builder.HasIndex(e => e.StartedAtUtc)
            .IsDescending();
        builder.HasIndex(e => e.Status);

        builder.ToTable(t =>
        {
            // The only status column in the schema that was free text. Every other one - orders,
            // refunds, vouchers, payments, devices, invitations, the outbox - is pinned to its enum by
            // a CHECK, and this one gates the entire intake: it decides whether an import is still
            // running, whether it finished, and whether it died halfway. A typo here is an import
            // that is invisible to whatever reads the column and will never be picked up again.
            //
            // The three values are what ImportVouchersCommand writes: Started on create, then
            // Completed or Failed.
            t.HasCheckConstraint("ck_voucher_imports_status",
                "status IN ('Started','Completed','Failed')");

            // A finished import has a finish time and a running one does not. Stated rather than
            // assumed, because these counters are what an operator reads to decide whether to re-run.
            t.HasCheckConstraint("ck_voucher_imports_completed",
                "(status = 'Started') = (completed_at_utc IS NULL)");
        });
    }
}
