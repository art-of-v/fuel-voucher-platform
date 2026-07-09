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
    }
}
