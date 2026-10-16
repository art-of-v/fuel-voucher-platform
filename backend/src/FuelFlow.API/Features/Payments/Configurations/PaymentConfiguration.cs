using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Payments.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(e => e.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.RefundId)
            .HasColumnName("refund_id");

        builder.Property(e => e.ParentPaymentId)
            .HasColumnName("parent_payment_id");

        builder.Property(e => e.Provider)
            .HasColumnName("provider")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.ProviderInvoiceId)
            .HasColumnName("provider_invoice_id")
            .HasMaxLength(100);

        builder.Property(e => e.ProviderPaymentId)
            .HasColumnName("provider_payment_id")
            .HasMaxLength(100);

        builder.Property(e => e.Merchant)
            .HasColumnName("merchant")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.AmountUah)
            .HasColumnName("amount_uah")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(e => e.AmountKopecks)
            .HasColumnName("amount_kopecks")
            .HasColumnType("numeric(14,0)")
            .IsRequired();

builder.Property(e => e.ProviderAmountKopecks)
            .HasColumnName("provider_amount_kopecks")
            .HasColumnType("numeric(14,0)");

        builder.Property(e => e.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.PaidAtUtc)
            .HasColumnName("paid_at_utc");

        builder.Property(e => e.WebhookEventId)
            .HasColumnName("webhook_event_id");

        builder.Property(e => e.ProviderPayload)
            .HasColumnName("provider_payload");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        // The provider-level identity. Deliberately NOT a unique constraint on order_id: an order can
        // legitimately have more than one capture row when a payment link is re-issued, and that is
        // exactly the case the table exists to make visible. A one-capture-per-order rule would be a
        // business decision, not a schema fact, so it is left unencoded until someone makes it.
        builder.HasIndex(e => new { e.Provider, e.ProviderInvoiceId, e.Kind })
            .IsUnique()
            .HasFilter("provider_invoice_id IS NOT NULL")
            .HasDatabaseName("ux_payments_provider_invoice_kind");

        builder.HasIndex(e => new { e.OrderId, e.Status })
            .HasDatabaseName("ix_payments_order_status");

        builder.HasIndex(e => e.ParentPaymentId)
            .HasDatabaseName("ix_payments_parent_payment_id");

        builder.HasIndex(e => e.WebhookEventId)
            .HasDatabaseName("ix_payments_webhook_event_id");

        // A refund points at a capture and never at itself; the two order FKs both Restrict for the
        // reason refunds.order_id does - a money row must not disappear because its order was purged.
        builder.HasOne(e => e.Order)
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Refund>()
            .WithMany()
            .HasForeignKey(e => e.RefundId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(e => e.ParentPaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        // WebhookEventId deliberately carries no FK, and the index above is deliberately not unique.
        // Same rule as the audit tables: this row has to stay writable whatever happened to the
        // delivery that produced it, and a webhook row pruned by retention must not take the payment
        // with it. It is still indexed, because "which delivery proved this payment" is a question
        // an operator asks during a dispute.

        builder.ToTable(t =>
        {
            // Only Succeeded is money. A refund of 0 or a negative charge is never legitimate.
            t.HasCheckConstraint("ck_payments_amount_positive", "amount_uah > 0 AND amount_kopecks > 0");

            // The two representations of one amount cannot drift. This is the constraint that makes
            // storing both defensible: without it, "UAH at checkout" and "kopecks on the wire" are
            // two numbers that quietly disagree, and the reconciliation report is what notices.
            // round() on numeric is half-away-from-zero, matching Money.ToKopecks exactly, so a value
            // written through Money cannot fail this - only a hand-converted one can.
            t.HasCheckConstraint("ck_payments_amount_units_agree",
                "amount_kopecks = round(amount_uah * 100)");

            // "Succeeded with no paid-at" is the state that makes a report lie: the revenue side
            // counts Succeeded, and a VAT period boundary needs a date. The reverse is deliberately
            // NOT constrained - a reversed payment was genuinely received and then returned, so
            // Cancelled with a paid_at is real and correct, and forbidding it would push the truth
            // somewhere less visible.
            t.HasCheckConstraint("ck_payments_succeeded_has_paid_at",
                "status <> 'Succeeded' OR paid_at_utc IS NOT NULL");

            t.HasCheckConstraint("ck_payments_kind",
                "kind IN ('Capture','Refund')");

            t.HasCheckConstraint("ck_payments_status",
                "status IN ('Pending','Succeeded','Failed','Cancelled','Expired')");

            t.HasCheckConstraint("ck_payments_currency", "currency = 'UAH'");

            // The acquirer reported amount is never asserted to match what we billed - see the
            // ProviderAmountKopecks remarks. Only its own shape is constrained, so a reversal cannot
            // land a negative charge.
            t.HasCheckConstraint("ck_payments_provider_amount_non_negative",
                "provider_amount_kopecks IS NULL OR provider_amount_kopecks > 0");

            // A refund is defined by what it reverses; a capture is not.
            t.HasCheckConstraint("ck_payments_refund_shape",
                "(kind = 'Refund' AND parent_payment_id IS NOT NULL) OR (kind = 'Capture')");

            t.HasCheckConstraint("ck_payments_parent_not_self", "parent_payment_id IS DISTINCT FROM id");

            t.HasCheckConstraint("ck_payments_provider_length", "length(provider) > 0 AND length(provider) <= 20");
        });
    }
}