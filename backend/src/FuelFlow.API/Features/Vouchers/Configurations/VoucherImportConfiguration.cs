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

        builder.Property(e => e.Id);

        builder.Property(e => e.FileName)
            .IsRequired();

        builder.Property(e => e.PageCount)
            .IsRequired();

        builder.Property(e => e.StartedAtUtc)
            .IsRequired();

        builder.Property(e => e.CompletedAtUtc);

        builder.Property(e => e.Status)
            .IsRequired();

        builder.Property(e => e.ImportedCount)
            .IsRequired();

        builder.Property(e => e.DuplicateCount)
            .IsRequired();

        builder.Property(e => e.FailedCount)
            .IsRequired();

        builder.Property(e => e.VerificationFailedCount)
            .IsRequired();

        builder.Property(e => e.VerifiedWithWarningsCount)
            .IsRequired();

        builder.HasIndex(e => e.StartedAtUtc)
            .IsDescending();
        builder.HasIndex(e => e.Status);
    }
}
