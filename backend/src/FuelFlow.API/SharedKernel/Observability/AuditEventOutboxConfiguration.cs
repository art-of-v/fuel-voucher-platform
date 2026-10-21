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
        builder.Property(e => e.AggregateType).HasColumnName("aggregate_type").HasMaxLength(100).IsRequired()
            .HasComment(                "What kind of thing changed - 'Refund', 'Voucher', and the other kinds the audit screen" +
                    "groups by. A short name, not a table name.");
        builder.Property(e => e.AggregateId).HasColumnName("aggregate_id").HasMaxLength(200).IsRequired()
            .HasComment(                "Which thing changed, by its own id - a uuid as a string.");
        builder.Property(e => e.ScopeId).HasColumnName("scope_id").HasMaxLength(200).IsRequired()
            .HasComment(                "The provider - the brand - the change happened within, so an audit read can be scoped to" +
                    "one.");
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired()
            .HasComment(                "What happened, in the past tense - 'RefundRequested', 'StatusChanged'.");
        builder.Property(e => e.OldValue).HasColumnName("old_value").HasColumnType("jsonb")
            .HasComment(                "The value before the change, as JSON. NULL when the thing was created: there was no" +
                    "value before.");
        builder.Property(e => e.NewValue).HasColumnName("new_value").HasColumnType("jsonb").IsRequired()
            .HasComment(                "The value after the change, as JSON.");
        builder.Property(e => e.ChangedByUserId).HasColumnName("changed_by_user_id")
            .HasComment(                "Who did it, when a person did. NULL when a machine did - and NULL rather than the" +
                    "all-zero uuid, so that 'no user' is a value that can still be checked against the users" +
                    "table.");
        builder.Property(e => e.ChangedByUserName).HasColumnName("changed_by_user_name").HasMaxLength(200)
            .HasComment(                "The name of the person who did it, kept on the row so the record still reads after the" +
                    "account is gone.");
        builder.Property(e => e.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired()
            .HasComment(                "One line, in the operator's words, saying what changed.");
        builder.Property(e => e.ChangedAtUtc).HasColumnName("changed_at_utc").HasColumnType("timestamp with time zone").IsRequired()
            .HasComment(                "When it happened.");
        // changed_by_user_id stays a bare uuid, like every other actor column on an audit row:
        // the record of who did something must be writable even if the account has since gone.
        builder.HasIndex(e => e.AggregateId);
        builder.HasIndex(e => e.ChangedAtUtc);
        // The admin audit screen filters by event type and orders newest-first; with no index on
        // event_type that query had to choose between a scan and a sort.
        builder.HasIndex(e => new { e.EventType, e.ChangedAtUtc })
            .IsDescending(false, true);
        // ChangedByUserId is indexed by the FK convention above.
    }
}
