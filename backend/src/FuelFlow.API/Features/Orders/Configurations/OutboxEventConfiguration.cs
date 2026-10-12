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

        // The value set OutboxEventType defines. A dispatcher switching on a typo'd value would
        // silently never fire, which is the worst shape a queue failure can take.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_outbox_event_type", """
                event_type IN ('OrderCreated', 'OrderFulfilled', 'OrderCancelled',
                               'PaymentCompleted', 'VoucherExpired', 'VoucherUsed', 'VoucherActivated')
                """);
        });
    }
}
