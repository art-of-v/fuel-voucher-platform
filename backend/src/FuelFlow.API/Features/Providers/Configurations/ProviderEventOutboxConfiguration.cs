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

        builder.Property(e => e.Id);
        builder.Property(e => e.AggregateType).HasColumnType("text").IsRequired();
        builder.Property(e => e.AggregateId).HasColumnType("text").IsRequired();
        builder.Property(e => e.EventType).HasColumnType("text").IsRequired();
        builder.Property(e => e.OldValue).HasColumnType("jsonb");
        builder.Property(e => e.NewValue).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.ChangedByUserId).IsRequired();
        builder.Property(e => e.ChangedByUserName).HasColumnType("text");
        builder.Property(e => e.Summary).HasColumnType("text").IsRequired();
        builder.Property(e => e.ChangedAtUtc).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(e => e.AggregateId);
        builder.HasIndex(e => e.ChangedAtUtc);
    }
}
