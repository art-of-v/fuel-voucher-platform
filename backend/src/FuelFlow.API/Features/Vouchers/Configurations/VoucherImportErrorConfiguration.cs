using FuelFlow.Features.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Configurations;

internal sealed class VoucherImportErrorConfiguration : IEntityTypeConfiguration<VoucherImportError>
{
    public void Configure(EntityTypeBuilder<VoucherImportError> builder)
    {
        builder.ToTable("voucher_import_errors");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id);

        builder.Property(e => e.ImportId)
            .IsRequired();

        builder.Property(e => e.PageNumber)
            .IsRequired();

        builder.Property(e => e.VoucherNumber)
            .HasMaxLength(100);

        builder.Property(e => e.ErrorMessage)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.RawText)
            .HasColumnType("text");

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        builder.HasOne<VoucherImport>()
            .WithMany()
            .HasForeignKey(d => d.ImportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.ImportId);
    }
}
