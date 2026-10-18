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

        // #246 detector: xmin token. No DDL - xmin is a system column. Raw SQL must never write
        // this table, or the token starts failing saves that did not actually race.
        //
        // This came across with the refunds this table absorbed. `refunds` had it and `payments` did
        // not, so for the life of the two-table migration a refund's concurrency protection lived on
        // the twin row rather than the one everything actually read and wrote. Two refund payments on
        // one order - a retry racing the status sync - could both save, and the loser's write would be
        // gone silently. With one table there is nothing to be protected on but this row.
        builder.UseXminAsConcurrencyToken();

        builder.Property(e => e.Id).HasColumnName("id");

        // Nullable: money can arrive before the order it is for. See Payment.OrderId for why a NULL here is
        // an advance rather than a defect - and for why it must never reach a sales account.
        builder.Property(e => e.OrderId)
            .HasColumnName("order_id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id");

        builder.Property(e => e.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

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

        builder.Property(e => e.CreatedByUserId)
            .HasColumnName("created_by_user_id");

        // 200 to match refunds.created_by_user_name, which this value replaced.
        builder.Property(e => e.CreatedByUserName)
            .HasColumnName("created_by_user_name")
            .HasMaxLength(200);

        builder.Property(e => e.IsAutomatic)
            .HasColumnName("is_automatic")
            .IsRequired();

        // 500 to match refunds.error_message, which this value replaced.
        builder.Property(e => e.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(500);

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

        // At most one refund per order - the guarantee `refunds.order_id` carried while that table
        // existed. A second concurrent refund request races past the existence check and is stopped
        // here, so the admin never opens two refunds for one order or gets a 500 for a double-click.
        //
        // Partial on kind = 'Refund', so this says nothing about captures: an order can still hold
        // several capture rows when a payment link is re-issued, which is the deliberate difference
        // from the refund side.
        builder.HasIndex(e => e.OrderId)
            .IsUnique()
            .HasFilter("kind = 'Refund'")
            .HasDatabaseName("ux_payments_one_refund_per_order");

        builder.HasIndex(e => e.WebhookEventId)
            .HasDatabaseName("ix_payments_webhook_event_id");

        // A refund points at a capture and never at itself; the two order FKs both Restrict for the
        // reason refunds.order_id does - a money row must not disappear because its order was purged.
        builder.HasOne(e => e.Order)
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Who the money is for, when we know. Restrict for the same reason as the order FK: a
        // customer's own payment ledger has to keep naming a real customer, so hard-deleting one must
        // fail loudly rather than leave balances pointing at nobody.
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // The operator's worklist. An unallocated payment is money we hold that belongs to nobody yet,
        // and nothing else in the system goes looking for it: it has no order, so no order report, no
        // reconciliation and no lifecycle job will ever surface it. Without this index it is found by
        // remembering to look, which is how held money goes missing for a month.
        builder.HasIndex(e => e.CreatedAtUtc)
            .HasDatabaseName("ix_payments_unallocated")
            .HasFilter("order_id IS NULL AND status = 'Succeeded'");

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(e => e.ParentPaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        // The approver of a refund, carried over from refunds.created_by_user_id with the same
        // Restrict: an audit row has to keep naming a real actor, so a user who has approved a refund
        // cannot be hard-deleted out from under it.
        builder.HasOne<FuelFlow.SharedKernel.Domain.User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
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

            // The approval columns arrived here from the `refunds` table. Only a refund is approved
            // by anyone, so a capture carrying an approver is a row that was written by the wrong
            // code path - and it is exactly the kind of thing that is invisible until someone asks
            // "who authorised this?" and gets two answers. is_automatic is included in the test
            // because false is its default: a capture must assert its value is the default, not
            // merely leave it unset.
            //
            // Scoped to the acquirer, and that scoping is the point. An acquirer-driven capture is
            // evidence the acquirer produced and no human touched, so "carries an approver" is
            // genuinely wrong for it. A DECLARED capture is the opposite case: a person stated that
            // money arrived, so an author is required and a row without one is unattributable money.
            // The original constraint would have refused exactly the payments that most need an audit
            // trail, and would have said nothing useful about the ones it was written for.
            t.HasCheckConstraint("ck_payments_refund_audit_only_on_refund",
                "kind = 'Refund' OR provider <> 'monobank' OR (created_by_user_id IS NULL " +
                "AND created_by_user_name IS NULL AND NOT is_automatic AND error_message IS NULL)");

            // Deliberately NO constraint pairing created_by_user_name with created_by_user_id. An
            // automatic refund has no approver at all, and a webhook-driven one carries a name the
            // id was not captured for; demanding an id would refuse refunds that really happened.
            // The FK does the work that matters: an id, when present, is a real user.
            t.HasCheckConstraint("ck_payments_provider_length", "length(provider) > 0 AND length(provider) <= 20");
        });
    }
}
