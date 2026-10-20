using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Orders.Configurations;

internal sealed class OrderEventConfiguration : IEntityTypeConfiguration<OrderEvent>
{
    public void Configure(EntityTypeBuilder<OrderEvent> builder)
    {
        // The value set OrderEvent.EventType documents. Added here so EF knows the constraint exists;
        // without it the same CHECK stays invisible to the model, which is F-06 / #245.
        builder.ToTable("order_events", t =>
        {
            t.HasCheckConstraint("ck_order_events_event_type",
                "event_type IN ('Created', 'StatusChanged', 'InvoiceCreated', 'PaymentCaptured', " +
                "'Fulfilled', 'PartiallyFulfilled', 'Refunded', 'Cancelled')");
            t.HasCheckConstraint("ck_order_events_from_to",
                "(event_type = 'Created' AND from_status IS NULL) " +
                "OR (event_type <> 'Created' AND to_status IS NOT NULL)");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.OrderId)
            .HasColumnName("order_id");

        builder.Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(e => e.FromStatus)
            .HasColumnName("from_status")
            .HasMaxLength(20);

        builder.Property(e => e.ToStatus)
            .HasColumnName("to_status")
            .HasMaxLength(20);

        builder.Property(e => e.ExternalRef)
            .HasColumnName("external_ref");

        builder.Property(e => e.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb");

        // Deliberately not a foreign key, on the same rule as every other audit actor column here -
        // see issue #307 for why that rule exists and what it does not permit.
        builder.Property(e => e.ActorUserId)
            .HasColumnName("actor_user_id");

        builder.Property(e => e.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .IsRequired();

        // The read path is always "this order, oldest first" - that is the entire point of the
        // table - and "every event for this external ref" is what a duplicate-delivery investigation
        // looks like.
        builder.HasIndex(e => new { e.OrderId, e.OccurredAtUtc });
        builder.HasIndex(e => e.ExternalRef);
        builder.HasIndex(e => e.OccurredAtUtc);

        // Cascade on the order: a deleted order takes its history with it. This is the one place a
        // cascade is right - an event without an order is unattributable, and the order's own row is
        // the only thing that could supply the missing half.
        builder.HasOne(e => e.Order)
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // No query filter and no IsDeleted. This table is append-only: nothing here is ever updated
        // or removed except by the cascade above, and a filter that hid deleted rows would defeat
        // the reason the table exists.

        // --- F-23: a reference column without an index ----------------------------------------------
        // the admin audit screen asks who did this. No foreign key here on purpose - this is an audit row, and an audit row must not
        // be the thing that blocks deleting its own subject - but the reference is still read, and
        // without an index every read of it is a scan.
        builder.HasIndex(e => e.ActorUserId);
    }
}
