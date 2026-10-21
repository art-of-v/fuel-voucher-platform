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

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.ImportId)
            .HasColumnName("import_id")
            .IsRequired().HasComment("The import this error belongs to. Every failed row points at the run that produced it, so" +
                    "one file's failures are read together.");

        builder.Property(e => e.PageNumber)
            .HasColumnName("page_number")
            .IsRequired().HasComment("The page of the file the row was read from. The page, not the row: a page is what a person" +
                    "can look at.");

        builder.Property(e => e.VoucherNumber)
            .HasColumnName("voucher_number")
            .HasMaxLength(100).HasComment("The voucher number as it was read, if it could be read at all. NULL when the line was" +
                    "unreadable - which is often exactly why it is an error.");

        builder.Property(e => e.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(2000)
            .IsRequired().HasComment("What went wrong, in words. The reason the row was refused.");

        // Bounded, and written through PrepareRawTextForStorage so the embedded QR payload - a
        // bearer instrument - never lands here verbatim.
        builder.Property(e => e.RawText)
            .HasColumnName("raw_text")
            .HasMaxLength(4000).HasComment("The line as it arrived, exactly as the file had it. The evidence: without it, nobody can" +
                    "tell whether the importer read the file wrong or the file was wrong.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired().HasComment("When the row was written.");

        builder.HasOne<VoucherImport>()
            .WithMany()
            .HasForeignKey(d => d.ImportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.ImportId);
    }
}
