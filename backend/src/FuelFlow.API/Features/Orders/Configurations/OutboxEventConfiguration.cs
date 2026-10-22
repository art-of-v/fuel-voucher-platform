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
            .IsRequired().HasComment("What kind of event this is - the routing key the publisher reads.");

        builder.Property(e => e.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired().HasComment("The event itself, as JSON: what the subscriber will receive, frozen at the moment it was" +
                    "written.");

        builder.Property(e => e.Processed)
            .HasColumnName("processed")
            .HasDefaultValue(false)
            .IsRequired().HasComment("Whether the event has been published. A row that says processed and carries no time" +
                    "cannot say when.");

        builder.Property(e => e.ProcessedAtUtc)
            .HasColumnName("processed_at_utc").HasComment("When it was published. NULL while it is not.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired().HasComment("When the event was written.");

        // The processed flag on its own - the plain one. Every reader of this table asks about
        // MORE than the flag (the dispatchers and the reconciliation report want the consumed
        // types, the purge wants the age), and each of them reads through an index of its own:
        // the partial one below, and the two at the end of this file.
        builder.HasIndex(e => e.Processed);

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

        // The retention purge's index: the events that are the record of something that happened,
        // aged by when it happened.
        //
        // WHY IT IS A DIFFERENT INDEX, AND WHY IT IS COMPOSITE. The purge asks for the events
        // that are OLD and that nobody consumes - `created_at_utc < cutoff AND event_type NOT IN
        // (the consumed types)`. The index above holds the opposite set (`event_type IN (the
        // consumed types)`), and a partial index cannot serve a predicate that contradicts its
        // filter: the planner saw that the query and the index's filter contradict each other
        // and used neither. So the purge had no index at all and read the whole table, every
        // night, to delete a few thousand rows out of two hundred thousand.
        //
        // EF Core cannot declare two indexes over the SAME columns either: a second HasIndex
        // over the same property set returns the FIRST index and edits it, which is how the
        // plain created_at_utc index became the partial one above and left nothing behind. A
        // composite is therefore the only way this model can carry a second index on that
        // column - and the id is free, because the row is deleted by its key anyway.
        builder.HasIndex(e => new { e.CreatedAtUtc, e.Id });

        // The retention purge's other index: the events that were PUBLISHED, aged by when they
        // were published. The purge asks for `processed AND processed_at_utc < cutoff`, and the
        // cutoff is on processed_at_utc - a column no index led, so this branch had nothing to
        // read through either.
        builder.HasIndex(e => e.ProcessedAtUtc);

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
