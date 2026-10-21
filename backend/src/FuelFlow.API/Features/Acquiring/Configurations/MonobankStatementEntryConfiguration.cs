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

        // String, not int, to match orders.monobank_merchant - the same enum stored two ways is a
        // conversion waiting to be forgotten, and an int enum silently changes meaning if someone
        // reorders the C# declaration, with no migration to catch it.
        builder.Property(e => e.Merchant)
            .HasColumnName("merchant")
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasComment(                "Which merchant's statement this came from. The same acquirer id can exist on both" +
                    "merchants, so the unique key is scoped by merchant rather than by id alone.");

        builder.Property(e => e.StatementId)
            .HasColumnName("statement_id")
            .HasMaxLength(200)
            .IsRequired()
            .HasComment(                "The key this row is filed under. OURS, composed from what the acquirer sends - the" +
                    "statement carries no id of its own - and stable across re-fetches of the same period," +
                    "which is what makes re-importing a month idempotent. It is printed in the act an" +
                    "accountant signs, so it is readable rather than a hash.");

        builder.Property(e => e.InvoiceId)
            .HasColumnName("invoice_id")
            .HasMaxLength(200)
            .HasComment(                "The acquirer's invoice id, when the row carries one.");

        // Indexed because it is the cross-match key the reconciler looks rows up by: the period read
        // filters on it, and neither that nor the per-invoice lookup should be a sequential scan of a
        // table that grows by a row per transaction, per merchant, forever. Reference is indexed
        // because it is the acquirer's echo of ours and operators do search the act by it - it is
        // never joined on, because it is not unique.
        builder.Property(e => e.Reference)
            .HasColumnName("reference")
            .HasMaxLength(200)
            .HasComment(                "Our reference, echoed back. Evidence, NOT a key - the same string appears on thousands" +
                    "of unrelated payments, and a match key built on it would match a customer's own memo to" +
                    "somebody else's invoice.");

        // The payment purpose as the acquirer shows it on the receipt. Evidence, like the reference
        // beside it and never a join key - the same purpose string appears on thousands of
        // unrelated payments, so a match key built on it would match a customer's own memo to
        // somebody else's invoice.
        builder.Property(e => e.Destination)
            .HasColumnName("destination")
            .HasMaxLength(200)
            .HasComment(                "The payment purpose as the acquirer shows it on the receipt. Evidence, and never a key," +
                    "for the same reason.");

builder.Property(e => e.AmountKopecks)
               .HasColumnName("amount_kopecks")
               .HasColumnType("numeric(14,0)")
    .HasComment(        "Kopecks, exactly as Monobank reports them - the acquirer's API speaks kopecks and" +
            "nothing else, so this is a boundary value and is not converted on the way in.");

        builder.Property(e => e.Ccy)
            .HasColumnName("ccy")
            .HasComment(                "The currency code, 980 for UAH.");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(50)
            .HasComment(                "The acquirer-side status, in the acquirer's vocabulary.");

        builder.Property(e => e.AcquirerCreatedAtUtc)
            .HasColumnName("acquirer_created_at_utc")
            .HasComment(                "The financial operation time (the 'date' on the wire). The period the act filters on.");

        builder.Property(e => e.AcquirerModifiedAtUtc)
            .HasColumnName("acquirer_modified_at_utc")
            .HasComment(                "Kept, and empty. The documented statement payload carries one timestamp per row and no" +
                    "separate modification time, so nothing populates this column today - it stays rather" +
                    "than being dropped so a future acquirer field does not need a migration to accept.");

        builder.Property(e => e.ExtRef)
            .HasColumnName("ext_ref")
            .HasMaxLength(200)
            .HasComment(                "The acquirer's own reference for the operation.");

        builder.Property(e => e.MaskedCard)
            .HasColumnName("masked_card")
            .HasMaxLength(50)
            .HasComment(                "The card, masked by the acquirer.");

        builder.Property(e => e.Rrn)
            .HasColumnName("rrn")
            .HasMaxLength(50)
            .HasComment(                "The acquirer's retrieval reference number.");

        builder.Property(e => e.ApprovalCode)
            .HasColumnName("approval_code")
            .HasMaxLength(50)
            .HasComment(                "The acquirer's approval code.");

        // Deliberately unindexed and unconstrained in length: this is an opaque blob kept for
        // re-derivation, not something the database needs to understand.
        builder.Property(e => e.RawPayload)
            .HasColumnName("raw_payload")
            .HasComment(                "The statement row exactly as received.");

        builder.Property(e => e.FirstSeenAtUtc)
            .HasColumnName("first_seen_at_utc")
            .IsRequired()
            .HasComment(                "When we first saw this row.");

        builder.Property(e => e.LastSeenAtUtc)
            .HasColumnName("last_seen_at_utc")
            .IsRequired().HasComment("When we last saw it in a fetch. A row that keeps being re-reported is one the acquirer is" +
                    "still revising, which is different from one that is settled and closed.");

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