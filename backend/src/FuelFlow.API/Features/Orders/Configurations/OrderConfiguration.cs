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
            .IsRequired();

        builder.Property(e => e.LegalEntityId)
            .HasColumnName("legal_entity_id");

        builder.Property(e => e.Price)
            .HasColumnName("price")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(OrderKind.Purchase)
            .IsRequired();

        builder.Property(e => e.SourceOrderId)
            .HasColumnName("source_order_id");

        builder.Property(e => e.MonobankInvoiceId)
            .HasColumnName("monobank_invoice_id")
            .HasMaxLength(100);

        builder.Property(e => e.MonobankStatus)
            .HasColumnName("monobank_status")
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.MonobankMerchant)
            .HasColumnName("monobank_merchant")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.MonobankPaymentUrl)
            .HasColumnName("monobank_payment_url")
            .HasMaxLength(500);

        builder.Property(e => e.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(150);

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

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
            .IsRequired();

        builder.Property(e => e.FulfilledAtUtc)
            .HasColumnName("fulfilled_at_utc");

        builder.Property(e => e.PartiallyFulfilledSinceUtc)
            .HasColumnName("partially_fulfilled_since_utc");

        // These two arrived in PascalCase because the mapping was forgotten - the only
        // snake_case violations left on this table. Named explicitly, like every other column.
        builder.Property(e => e.LastWebhookProcessedAtUtc)
            .HasColumnName("last_webhook_processed_at_utc");

        builder.Property(e => e.LastWebhookModifiedDateUtc)
            .HasColumnName("last_webhook_modified_date_utc");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired();

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
