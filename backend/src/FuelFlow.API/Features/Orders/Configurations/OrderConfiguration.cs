using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Orders.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(e => e.Id);

        // #246: xmin token. No DDL — xmin is a system column. One raw statement writes this table
        // (FulfillmentService.TryMarkOrderFulfilledAsync) and it is a compare-and-swap; the tracked
        // writes around it read their row fresh, so a conflict here is a race that really happened.
        builder.UseXminAsConcurrencyToken();

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired().HasComment("The customer who placed the order.");

        builder.Property(e => e.LegalEntityId)
            .HasColumnName("legal_entity_id").HasComment("The company the order is for. NULL for an order placed by a person without a company.");

        builder.Property(e => e.Price)
            .HasColumnName("price")
            .HasColumnType("numeric(12,2)")
            .IsRequired().HasComment("What the customer is charged for the order, in UAH. The order's own number - the line" +
                    "totals are what the price is made of, and this is what was agreed.");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired().HasComment("Where the order stands: new, partly fulfilled, fulfilled, cancelled. The screens read" +
                    "this column, and the database holds it to the enum.");

        builder.Property(e => e.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(OrderKind.Purchase)
            .IsRequired().HasComment("What this order represents: a purchase, a renewal, or an issuance. Everything created" +
                    "before this column existed is a purchase.");

        builder.Property(e => e.SourceOrderId)
            .HasColumnName("source_order_id").HasComment("For an issuance, the purchase the fuel originally came from. NULL on purchases and" +
                    "renewals.");

        builder.Property(e => e.MonobankInvoiceId)
            .HasColumnName("monobank_invoice_id")
            .HasMaxLength(100).HasComment("The invoice id the acquirer gave us - the link back to the acquirer's own record. NULL" +
                    "until the invoice is created.");

        // The brand's накладна for an off-platform renewal (planning #319). The renewal is an order
        // like any other, so the document the customer paid against belongs with the order's other
        // money columns - and a self-serve order has none, because the acquirer's invoice is
        // monobank_invoice_id.
        builder.Property(e => e.ExternalInvoiceNumber)
            .HasColumnName("external_invoice_number")
            .HasMaxLength(100)
            .HasComment("The brand's invoice for an off-platform renewal - the document the customer paid against. NULL for" +
                    "a self-serve order, where the acquirer's invoice is monobank_invoice_id.");

        builder.Property(e => e.ExternalInvoiceDate)
            .HasColumnName("external_invoice_date")
            .HasComment("The date on that invoice. NULL with it.");

        builder.Property(e => e.MonobankStatus)
            .HasColumnName("monobank_status")
            .HasConversion<string>()
            .HasMaxLength(50).HasComment("The acquirer's state of the payment, as the acquirer reports it. NULL while nothing is" +
                    "known.");

        builder.Property(e => e.MonobankMerchant)
            .HasColumnName("monobank_merchant")
            .HasConversion<string>()
            .HasMaxLength(20).HasComment("Which merchant the acquirer says the payment went to. The acquirer's word, not ours -" +
                    "which is why it is compared against what we expect rather than being our own field.");

        builder.Property(e => e.MonobankPaymentUrl)
            .HasColumnName("monobank_payment_url")
            .HasMaxLength(500).HasComment("The payment link the customer is given. It is handed out, not derived: a link that could" +
                    "be recomputed would not be a link the acquirer knows.");

        // This order's own key, and the acquirer's external reference for its refund. The comment it
        // used to carry said the key was "the key the customer sent with the request" and that "two
        // requests with the same key are one order" - and no customer has ever sent one: the value is
        // derived here, it carries a random suffix, and the customer cannot repeat it. The checkout's
        // identity is checkout_key, below; this column stays the per-order token.
        builder.Property(e => e.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(150).HasComment("This order's own key: unique per order, and the external reference the acquirer is given when the order is refunded. The checkout's identity is checkout_key - two requests for one basket are one order through that column, not through this one.");

        // The basket this order is the live attempt for. The index below is what makes a double tap
        // one order instead of two: the second insert for the same basket is refused by the database
        // rather than by a read-then-write race in the handler.
        //
        // The key names WHAT was bought and by whom, and nothing else: no clock, no random suffix.
        // The minute of the clock used to be in the key, rounded down to a five-minute bucket, which
        // made the window discontinuous - a retry one millisecond before the bucket rolled over
        // produced a different key, the dedupe missed, and the customer was charged twice for one
        // tap.
        //
        // The filter is what makes a repeat purchase possible: only an order that still awaits
        // payment holds the basket, so an order that has been paid (or cancelled, or deleted) gives
        // the basket back and the customer can buy the same fuel again.
        builder.Property(e => e.CheckoutKey)
            .HasColumnName("checkout_key")
            .HasMaxLength(150).HasComment("The basket this order is the live attempt for: the customer, the brand, the fuel, the litres and the quantity (a digest of the whole cart for a bulk checkout). Two orders may not hold the same basket while both await payment, so a double tap and a retried request are one order and one invoice.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired().HasComment("When the row was written.");

        // The default used to be '-infinity', and it was not chosen: it is what Npgsql writes for
        // DateTime.MinValue, which is what EF's convention put into the migrations that created this
        // column. -infinity sorts before every real timestamp and serialises badly, and it is a value
        // no application means.
        //
        // now() is the honest fallback for a raw INSERT that does not state the timestamp, and
        // ValueGeneratedNever keeps the application in charge: it still sends its own value on every
        // insert, exactly as it did before, so nothing about the application's behaviour changes. The
        // default is a safety net for SQL that does not come from EF.
        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasDefaultValueSql("now()")
            .ValueGeneratedNever()
            .IsRequired().HasComment("When it was last changed.");

        builder.Property(e => e.FulfilledAtUtc)
            .HasColumnName("fulfilled_at_utc").HasComment("When the order was fulfilled. NULL while it is not.");

        builder.Property(e => e.PartiallyFulfilledSinceUtc)
            .HasColumnName("partially_fulfilled_since_utc").HasComment("Since when the order has been partly fulfilled. A row that says 'partly fulfilled' and no" +
                    "date cannot say how long it has been waiting.");

        // These two arrived in PascalCase because the mapping was forgotten - the only
        // snake_case violations left on this table. Named explicitly, like every other column.
        builder.Property(e => e.LastWebhookProcessedAtUtc)
            .HasColumnName("last_webhook_processed_at_utc").HasComment("The last webhook of the acquirer we have processed - the clock that says we are not" +
                    "behind.");

        builder.Property(e => e.LastWebhookModifiedDateUtc)
            .HasColumnName("last_webhook_modified_date_utc").HasComment("The modification date of the acquirer's own record, as it was when we last read it. The" +
                    "acquirer's clock, not ours - which is what makes it worth storing.");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired().HasComment("Soft delete. The row is hidden, not erased: the orders, the money and the vouchers behind" +
                    "it have to stay readable.");

        builder.HasQueryFilter(e => !e.IsDeleted);

        builder.HasOne<FuelFlow.Features.Contracts.SharedModels.LegalEntity>()
            .WithMany()
            .HasForeignKey(e => e.LegalEntityId)
            .OnDelete(DeleteBehavior.SetNull);

        // Self-reference for issuances: an issuance points at the purchase its fuel came from.
        // SetNull rather than Cascade so deleting one order can never cascade into another.
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(e => e.SourceOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        // LegalEntityId and SourceOrderId are FK columns and need their own index: without one, any
        // delete or update on the parent scans this table. CreatedAtUtc stays standalone because
        // (UserId, CreatedAtUtc) has UserId leading, so it does not serve a date range across
        // users. Bare UserId is a leftmost prefix of both composites and is dropped; bare Status
        // is 8 values and is dropped with it.
        builder.HasIndex(e => e.LegalEntityId);
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => e.SourceOrderId);
        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL");

        // The checkout's guard, and the only index in this schema that decides a MONEY question:
        // one basket, one order awaiting payment. Everything else about an order is written by one
        // writer; this one is written by two requests that may not know about each other - a double
        // tap, or the client's automatic resend of the same POST while the first attempt is still
        // running.
        //
        // Unique, so the second insert is refused by the database instead of by a read-then-write in
        // the handler: the handler's read could pass for both requests, and then both would mint an
        // invoice and the customer would be charged twice for one basket.
        //
        // Partial on the two states that end an attempt, so the basket is given back: an order that
        // is no longer awaiting payment (paid, cancelled, fulfilled) and an order the customer
        // deleted. Without that, a customer could buy the same fuel exactly once, ever.
        builder.HasIndex(e => e.CheckoutKey)
            .IsUnique()
            .HasFilter("checkout_key IS NOT NULL AND status = 'PendingPayment' AND is_deleted = false");

        // Unique as well as indexed, and both halves matter:
        //
        // Indexed - ProcessMonobankWebhookCommandHandler resolves the order by this column on the
        // ANONYMOUS webhook path, with no index at all it was a sequential scan of orders per
        // delivery. Reconciliation matches on it too (GetStatementReconciliationQueryHandler,
        // StatementMatcher).
        //
        // Unique - that handler reads it with FirstOrDefaultAsync, so two orders sharing an
        // invoice id would let a payment fulfil whichever row the plan happened to return, with no
        // error anywhere. An order is what Monobank actually charged against, so allowing two to
        // claim the same invoice is a money correctness bug, not a tidy-up.
        //
        // Partial, like idempotency_key above: an order that has not reached the payment provider
        // yet has no invoice, and thousands of nulls must not collide with each other. Postgres
        // treats nulls as distinct in a unique index anyway, but the filter keeps the intent
        // explicit and matches the sibling constraint.
        builder.HasIndex(e => e.MonobankInvoiceId)
            .IsUnique()
            .HasFilter("monobank_invoice_id IS NOT NULL");
        builder.HasIndex(e => new { e.UserId, e.Status });
        builder.HasIndex(e => new { e.UserId, e.CreatedAtUtc })
            .IsDescending(false, true);

        // The index names are no longer written out here. EF's default for this index is
        // IX_orders_monobank_invoice_id, and ApplicationDbContext's naming convention turns that
        // into ix_orders_monobank_invoice_id - which is the name the database now carries, after
        // 20261019131002_IndexNamesFollowOneConvention renamed it.
        //
        // The explicit HasDatabaseName this replaced was there for a real reason, and the reason
        // still holds: UniqueMonobankInvoiceId created the index as IX_orders_monobank_invoice_id
        // (snake_case, as migrationBuilder names things) while EF's convention would have
        // scaffolded a different one, so the model and the database described two different
        // indexes and the *next* migration added for any reason would have failed on a fresh
        // database with 42P07 "relation ... already exists". What changed is that the name now
        // comes from one convention rather than from a line that had to be kept in step with
        // whatever a migration happened to create - and that convention is applied to the model,
        // so the two cannot drift apart again.
        //
        // IX_orders_user_id was a strict leftmost prefix of both composites above and was dropped
        // by F-11; the row it was for is served by the two composites.

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_order_price_non_negative", "price >= 0");

            // The value sets the C# enums define. Storing a closed enum as text is the right call -
            // it survives adding a member, unlike an int - but only once the database knows the
            // set. Without these, any 30-character string is a valid status and a typo round-trips
            // into nowhere: reading it back yields a value no enum member matches.
            t.HasCheckConstraint("ck_order_status", """
                status IN ('PendingPayment', 'Paid', 'PendingFulfillment', 'PartiallyFulfilled',
                           'Fulfilled', 'PartiallyRefunded', 'Refunded', 'Cancelled')
                """);

            t.HasCheckConstraint("ck_order_kind",
                "kind IN ('Purchase', 'Renewal', 'ReceivedFromCompany')");
        });
    }
}
