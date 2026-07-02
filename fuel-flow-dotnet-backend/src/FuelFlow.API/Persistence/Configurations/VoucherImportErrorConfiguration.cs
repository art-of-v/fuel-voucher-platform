using FuelFlow.Features.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Persistence.Configurations;

internal sealed class VoucherImportErrorConfiguration : IEntityTypeConfiguration<VoucherImportError>
{
    public void Configure(EntityTypeBuilder<VoucherImportError> builder)
    {
        builder.ToTable("voucher_import_errors");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.ImportId)
            .HasColumnName("import_id")
            .IsRequired();

        builder.Property(e => e.PageNumber)
            .HasColumnName("page_number")
            .IsRequired();

        builder.Property(e => e.VoucherNumber)
            .HasColumnName("voucher_number")
            .HasMaxLength(100);

        builder.Property(e => e.ErrorMessage)
            .HasColumnName("error_message")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.RawText)
            .HasColumnName("raw_text")
            .HasColumnType("text");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasOne<VoucherImport>()
            .WithMany()
            .HasForeignKey(d => d.ImportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
