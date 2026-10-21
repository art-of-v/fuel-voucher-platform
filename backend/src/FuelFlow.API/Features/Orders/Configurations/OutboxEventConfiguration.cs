using FuelFlow.Features.Orders.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Orders.Configurations;

internal sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasConversion<string>()
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.Processed)
            .HasColumnName("processed")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(e => e.ProcessedAtUtc)
            .HasColumnName("processed_at_utc");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(e => e.Processed);
        builder.HasIndex(e => e.CreatedAtUtc);

        // The safety net's own index: the events that are waiting for a consumer, and only those.
        //
        // WHAT WAS WRONG. Three jobs read this table every five minutes - the fulfilment service
        // (OrderCreated) and the two notification passes (OrderFulfilled and VoucherIssued) - and
        // each asks for the events of its own type that are still unprocessed and older than the
        // safety-net window. The table keeps thirty days of events, and FIVE of the eight event
        // types have no consumer at all: they are the audit trail, and they stay unprocessed for
        // good, because marking them processed would be a lie about work nobody did.
        //
        // So "unprocessed" is not a small set, and the only index that could serve the query - the
        // one on created_at_utc, which also provides the ordering - led the planner through the
        // whole thirty-day window, one row at a time, to find the handful of events that actually
        // needed it. Measured on a table of 200 000 rows spread over thirty days, in the shape the
        // table really has:
        //
        //     199 951 rows visited, 0 found, 37.0 ms
        //
        // And with this index, the same query:
        //
        //     5 rows visited, 0 found, 0.034 ms
        //
        // The index holds only what the dispatchers are looking for, so it is small - and it is
        // small for the same reason the query is fast.
        //
        // WHY THE TYPE LIST, AND WHAT TO DO WHEN IT CHANGES. The types that have a consumer are
        // the thing that keeps the index small: an index on `processed = false` alone would hold
        // the five consumer-less types as well, which is most of the table, and the safety net
        // would still walk the window. The list is ConsumedOutboxEventTypes - the same list the
        // admin's reconciliation report counts, so the two cannot drift apart. Adding an event
        // type WITH a consumer means adding it there. A type that is missing from the list still
        // works - it just keeps the old plan.
        builder.HasIndex(e => e.CreatedAtUtc)
            .HasDatabaseName("ix_outbox_events_pending_consumed")
            .HasFilter(
                "processed = false AND event_type IN ("
                + string.Join(", ", ConsumedOutboxEventTypes.All.Select(t => $"'{t}'"))
                + ")");

        // The value set OutboxEventType defines. A dispatcher switching on a typo'd value would
        // silently never fire, which is the worst shape a queue failure can take.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_outbox_event_type", """
                event_type IN ('OrderCreated', 'OrderFulfilled', 'OrderCancelled',
                               'PaymentCompleted', 'VoucherExpired', 'VoucherUsed', 'VoucherActivated',
                               'VoucherIssued')
                """);
        });
    }
}
