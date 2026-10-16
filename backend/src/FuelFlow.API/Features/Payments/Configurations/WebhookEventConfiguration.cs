using FuelFlow.Payments.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Payments.Configurations;

internal sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("webhook_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Provider)
            .HasColumnName("provider")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.ProviderEventId)
            .HasColumnName("provider_event_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.ProviderStatus)
            .HasColumnName("provider_status")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.InvoiceId)
            .HasColumnName("invoice_id")
            .HasMaxLength(100);

        builder.Property(e => e.AmountKopecks)
            .HasColumnName("amount_kopecks")
            .HasColumnType("numeric(14,0)");

        builder.Property(e => e.ProviderCreatedAtUtc)
            .HasColumnName("provider_created_at_utc");

        builder.Property(e => e.ProviderModifiedAtUtc)
            .HasColumnName("provider_modified_at_utc");

        // jsonb, like every other provider document in this schema (monobank_statement_entries.
        // raw_payload, provider_event_outbox.new_value). Nullable would be wrong: a stored delivery
        // with no body is not evidence of anything.
        builder.Property(e => e.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.Signature)
            .HasColumnName("signature")
            .HasMaxLength(500);

        builder.Property(e => e.KeyId)
            .HasColumnName("key_id")
            .HasMaxLength(100);

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.ReceivedAtUtc)
            .HasColumnName("received_at_utc")
            .IsRequired();

        builder.Property(e => e.ProcessedAtUtc)
            .HasColumnName("processed_at_utc");

        builder.Property(e => e.Attempts)
            .HasColumnName("attempts")
            .IsRequired();

        builder.Property(e => e.Error)
            .HasColumnName("error")
            .HasMaxLength(1000);

        // The idempotency guarantee. A redelivery hits this and Postgres answers 23505, so the
        // database refuses to hold the same provider event twice rather than a handler deciding
        // it looks like a replay. See WebhookEventIdentity for how the id is derived when the
        // provider supplies none.
        builder.HasIndex(e => new { e.Provider, e.ProviderEventId })
            .IsUnique()
            .HasDatabaseName("ux_webhook_events_provider_event");

        // Every question about an event starts from the invoice, and the retention job and the
        // unprocessed sweep both filter on it.
        builder.HasIndex(e => e.InvoiceId)
            .HasDatabaseName("ix_webhook_events_invoice_id");

        builder.HasIndex(e => e.ProviderStatus)
            .HasDatabaseName("ix_webhook_events_provider_status");

        // Partial, because the overwhelmingly common case is a processed row and a full index over
        // it would be pure write amplification on a table that grows with every payment.
        builder.HasIndex(e => e.ReceivedAtUtc)
            .HasFilter("status IN ('Received','Failed')")
            .HasDatabaseName("ix_webhook_events_outstanding");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_webhook_events_status",
                "status IN ('Received','Processed','Ignored','Failed')");

            t.HasCheckConstraint("ck_webhook_events_attempts", "attempts >= 1");

            // A settled delivery has a settled timestamp; an outstanding one does not. The reverse
            // direction is what "processed at" means, so it is the one worth a constraint.
            t.HasCheckConstraint("ck_webhook_events_processed",
                "status NOT IN ('Processed','Ignored') OR processed_at_utc IS NOT NULL");

            // provider_event_id is a hex SHA-256 from WebhookEventIdentity.Derive, which returns
            // null rather than inventing one for an event it cannot identify.
            t.HasCheckConstraint("ck_webhook_events_event_id_length", "length(provider_event_id) = 64");

            t.HasCheckConstraint("ck_webhook_events_amount_non_negative",
                "amount_kopecks IS NULL OR amount_kopecks > 0");
        });
    }
}