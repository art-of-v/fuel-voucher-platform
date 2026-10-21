using FuelFlow.Features.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Configurations;

internal sealed class QrParametersConfiguration : IEntityTypeConfiguration<QrParameters>
{
    public void Configure(EntityTypeBuilder<QrParameters> builder)
    {
        builder.ToTable("qr_parameters");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Version)
            .HasColumnName("version").HasComment("The QR code version (1-40), read from the aligned bit-matrix width. It controls the grid" +
                    "density.");

        builder.Property(e => e.EccLevel)
            .HasColumnName("ecc_level")
            .HasMaxLength(1)
            .IsRequired().HasComment("The error correction level of the original QR: L, M, Q or H.");

        builder.Property(e => e.MaskPattern)
            .HasColumnName("mask_pattern").HasComment("The data-mask reference number (0-7), read from the format-information strip.");

        builder.Property(e => e.EncodingMode)
            .HasColumnName("encoding_mode")
            .HasMaxLength(16).HasComment("The data-encoding mode of the original QR: NUMERIC, ALPHANUMERIC or BYTE. A hint when the" +
                    "code is regenerated.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired().HasComment("When the row was written.");

        // NULLS NOT DISTINCT, and it is not a style choice. Three of the four columns are nullable, and
// Postgres treats NULLs as distinct in a plain unique btree - so `('M', NULL, NULL, NULL)` could be
// inserted twice, three times, once per import that failed to read an ECC level. The index looked
// like it guaranteed one row per QR configuration and guaranteed nothing.
//
// This is the same defect F-05 reported and that fix papered over: it corrected the lookup so the
// leak stopped, and left an index that never could have caught it. Two imports producing the same
// configuration still produce two rows here, which is what the table is supposed to make impossible.
//
// PG15+, and the project is on 16. A failure this migration raises is a real duplicate, and the
// operator message names the constraint so it can be looked at rather than guessed at.
builder.HasIndex(e => new { e.EccLevel, e.Version, e.MaskPattern, e.EncodingMode })
    .IsUnique()
    .AreNullsDistinct(false);

builder.ToTable(t =>
{
    // The doc comments on QrParameters already promise these three ranges and nothing enforced
    // them - a row could say ECC 'Z' or version 999 and the import would store it, and the value
    // would then be rendered into a QR that no scanner reads. The values come from parsing
    // supplier PDFs, so this is not a theoretical input.
    t.HasCheckConstraint("ck_qr_parameters_ecc_level", "ecc_level IN ('L','M','Q','H')");

    // 1-40 is the QR standard's own range for the symbol version.
    t.HasCheckConstraint("ck_qr_parameters_version", "version IS NULL OR version BETWEEN 1 AND 40");

    // 0-7 are the eight data-mask patterns the standard defines.
    t.HasCheckConstraint("ck_qr_parameters_mask_pattern", "mask_pattern IS NULL OR mask_pattern BETWEEN 0 AND 7");
});
    }
}
