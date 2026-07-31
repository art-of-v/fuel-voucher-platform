using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Providers.Configurations;

internal sealed class ProviderEventOutboxConfiguration : IEntityTypeConfiguration<ProviderEventOutbox>
{
    public void Configure(EntityTypeBuilder<ProviderEventOutbox> builder)
    {
        builder.ToTable("provider_event_outbox");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.AggregateType).HasColumnName("aggregate_type").HasColumnType("text").IsRequired();
        builder.Property(e => e.AggregateId).HasColumnName("aggregate_id").HasColumnType("text").IsRequired();
        builder.Property(e => e.ProviderId).HasColumnName("provider_id").HasColumnType("text").IsRequired();
        builder.Property(e => e.EventType).HasColumnName("event_type").HasColumnType("text").IsRequired();
        builder.Property(e => e.OldValue).HasColumnName("old_value").HasColumnType("jsonb");
        builder.Property(e => e.NewValue).HasColumnName("new_value").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.ChangedByUserId).HasColumnName("changed_by_user_id").IsRequired();
        builder.Property(e => e.ChangedByUserName).HasColumnName("changed_by_user_name").HasColumnType("text");
        builder.Property(e => e.Summary).HasColumnName("summary").HasColumnType("text").IsRequired();
        builder.Property(e => e.ChangedAtUtc).HasColumnName("changed_at_utc").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(e => e.AggregateId);
        builder.HasIndex(e => e.ChangedAtUtc);
    }
}
