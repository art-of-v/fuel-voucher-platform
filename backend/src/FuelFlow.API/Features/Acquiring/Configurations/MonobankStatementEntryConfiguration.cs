using FuelFlow.Features.Acquiring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Acquiring.Configurations;

internal sealed class MonobankStatementEntryConfiguration : IEntityTypeConfiguration<MonobankStatementEntry>
{
    public void Configure(EntityTypeBuilder<MonobankStatementEntry> builder)
    {
        builder.ToTable("monobank_statement_entries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Merchant)
            .HasColumnName("merchant")
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.StatementId)
            .HasColumnName("statement_id")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.InvoiceId)
            .HasColumnName("invoice_id")
            .HasMaxLength(200);

        // Indexed because it is the primary cross-match key: the reconciler looks up by reference
        // first and by invoice second, and neither lookup should be a sequential scan of a table
        // that grows by a row per transaction, per merchant, forever.
        builder.Property(e => e.Reference)
            .HasColumnName("reference")
            .HasMaxLength(200);

        builder.Property(e => e.AmountKopecks)
            .HasColumnName("amount_kopecks");

        builder.Property(e => e.Ccy)
            .HasColumnName("ccy");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(50);

        builder.Property(e => e.AcquirerCreatedAtUtc)
            .HasColumnName("acquirer_created_at_utc");

        builder.Property(e => e.AcquirerModifiedAtUtc)
            .HasColumnName("acquirer_modified_at_utc");

        builder.Property(e => e.ExtRef)
            .HasColumnName("ext_ref")
            .HasMaxLength(200);

        builder.Property(e => e.MaskedCard)
            .HasColumnName("masked_card")
            .HasMaxLength(50);

        builder.Property(e => e.Rrn)
            .HasColumnName("rrn")
            .HasMaxLength(50);

        builder.Property(e => e.ApprovalCode)
            .HasColumnName("approval_code")
            .HasMaxLength(50);

        // Deliberately unindexed and unconstrained in length: this is an opaque blob kept for
        // re-derivation, not something the database needs to understand.
        builder.Property(e => e.RawPayload)
            .HasColumnName("raw_payload");

        builder.Property(e => e.FirstSeenAtUtc)
            .HasColumnName("first_seen_at_utc")
            .IsRequired();

        builder.Property(e => e.LastSeenAtUtc)
            .HasColumnName("last_seen_at_utc")
            .IsRequired();

        // The idempotency guarantee for re-importing a period. Scoped by merchant because the same
        // acquirer statement id can legitimately appear under both merchants, and without the
        // merchant in the key the second import would collide with the first and throw.
        builder.HasIndex(e => new { e.Merchant, e.StatementId })
            .IsUnique();

        // Serves the per-period reconciliation read, and the cross-match lookups.
        builder.HasIndex(e => new { e.Merchant, e.AcquirerCreatedAtUtc });
        builder.HasIndex(e => e.InvoiceId);
        builder.HasIndex(e => e.Reference);
    }
}