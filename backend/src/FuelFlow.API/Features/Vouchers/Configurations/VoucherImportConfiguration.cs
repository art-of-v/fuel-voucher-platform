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

        builder.Property(e => e.VerificationFailedCount)
            .HasColumnName("verification_failed_count")
            .IsRequired().HasComment("How many rows were read but did not verify - the code does not match the fuel, the litres do" +
                    "not match the code.");

        builder.Property(e => e.VerifiedWithWarningsCount)
            .HasColumnName("verified_with_warnings_count")
            .IsRequired().HasComment("How many rows verified but with a warning - read and checked, with something odd that did not" +
                    "stop them.");

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
