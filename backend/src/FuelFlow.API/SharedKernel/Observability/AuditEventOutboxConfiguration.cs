using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Providers.Configurations;

internal sealed class AuditEventOutboxConfiguration : IEntityTypeConfiguration<AuditEventOutbox>
{
    public void Configure(EntityTypeBuilder<AuditEventOutbox> builder)
    {
        builder.ToTable("provider_event_outbox");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.AggregateType).HasColumnName("aggregate_type").HasMaxLength(100).IsRequired();
        builder.Property(e => e.AggregateId).HasColumnName("aggregate_id").HasMaxLength(200).IsRequired();
        builder.Property(e => e.ScopeId).HasColumnName("scope_id").HasMaxLength(200).IsRequired();
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(e => e.OldValue).HasColumnName("old_value").HasColumnType("jsonb");
        builder.Property(e => e.NewValue).HasColumnName("new_value").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.ChangedByUserId).HasColumnName("changed_by_user_id");
        builder.Property(e => e.ChangedByUserName).HasColumnName("changed_by_user_name").HasMaxLength(200);
        builder.Property(e => e.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired();
        builder.Property(e => e.ChangedAtUtc).HasColumnName("changed_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(e => e.AggregateId);
        builder.HasIndex(e => e.ChangedAtUtc);
        // The admin audit screen filters by event type and orders newest-first; with no index on
        // event_type that query had to choose between a scan and a sort.
        builder.HasIndex(e => new { e.EventType, e.ChangedAtUtc })
            .IsDescending(false, true);

        // --- F-14: the foreign key this column was missing -------------------------------------------------
        // The column used to be NOT NULL and the price sync wrote the all-zero guid into it, which
        // is why it had no foreign key: a value that stands for "no user" cannot point at a user.
        // The system now writes NULL for that, and the foreign key is possible - with SET NULL,
        // because the record of what a machine did has to survive the account it names, and the
        // account may go.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.ChangedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Postgres does not index a foreign key column on its own, and the parent's delete check and
        // every join down this column scan without one.
        builder.HasIndex(e => e.ChangedByUserId);
    }
}
