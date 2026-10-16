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
            .IsRequired();

        builder.Property(e => e.PageCount)
            .HasColumnName("page_count")
            .IsRequired();

        builder.Property(e => e.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .IsRequired();

        builder.Property(e => e.CompletedAtUtc)
            .HasColumnName("completed_at_utc");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.ImportedCount)
            .HasColumnName("imported_count")
            .IsRequired();

        builder.Property(e => e.DuplicateCount)
            .HasColumnName("duplicate_count")
            .IsRequired();

        builder.Property(e => e.FailedCount)
            .HasColumnName("failed_count")
            .IsRequired();

        builder.Property(e => e.VerificationFailedCount)
            .HasColumnName("verification_failed_count")
            .IsRequired();

        builder.Property(e => e.VerifiedWithWarningsCount)
            .HasColumnName("verified_with_warnings_count")
            .IsRequired();

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
