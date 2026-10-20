using FuelFlow.Features.Settlement.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Settlement.Configurations;

internal sealed class SettlementMessageConfiguration : IEntityTypeConfiguration<SettlementMessage>
{
    public void Configure(EntityTypeBuilder<SettlementMessage> builder)
    {
        builder.ToTable("settlement_messages");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        // Bigint, not a uuid: these are Telegram's own identifiers and nothing else produces them.
        // Storing them as text would make every lookup a cast and buy nothing.
        builder.Property(e => e.ChatId)
            .HasColumnName("chat_id")
            .IsRequired();

        builder.Property(e => e.MessageId)
            .HasColumnName("message_id")
            .IsRequired();

        builder.Property(e => e.TelegramUserId)
            .HasColumnName("telegram_user_id")
            .IsRequired();

        builder.Property(e => e.TelegramDisplayName)
            .HasColumnName("telegram_display_name")
            .HasMaxLength(200);

        // 1000 is Telegram's own message ceiling, and this column only ever holds a one-line
        // declaration. Bounded so a hostile paste cannot become an unbounded row.
        builder.Property(e => e.Text)
            .HasColumnName("text")
            .HasMaxLength(1000);

        builder.Property(e => e.SentAtUtc)
            .HasColumnName("sent_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.Outcome)
            .HasColumnName("outcome")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasMaxLength(500);

        builder.Property(e => e.AmountUah)
            .HasColumnName("amount_uah")
            .HasColumnType("numeric(12,2)");

        builder.Property(e => e.Code)
            .HasColumnName("code")
            .HasMaxLength(8);

        builder.Property(e => e.ResolvedUserId)
            .HasColumnName("resolved_user_id");

        // The actor column is read, so it is indexed: Postgres does not index a column on its own.
        builder.HasIndex(e => e.ResolvedUserId);

        builder.Property(e => e.PaymentId)
            .HasColumnName("payment_id");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // The idempotency key, and the reason this table exists rather than a log line. Telegram
        // redelivers an update on any non-2xx, and a redelivery that created a second payment would
        // not be a duplicate message - it would be money twice. Uniqueness in the database is the
        // only thing that holds when two deliveries race.
        builder.HasIndex(e => new { e.ChatId, e.MessageId })
            .IsUnique()
            .HasDatabaseName("ux_settlement_messages_chat_message");

        // The channel's audit view: everything this sender ever tried, newest first. Needed the
        // moment someone asks who posted something.
        builder.HasIndex(e => new { e.TelegramUserId, e.SentAtUtc })
            .HasDatabaseName("ix_settlement_messages_sender_time");

        // The exception list. A message this bot refused is a row nothing else will ever surface, and
        // a sender being refused repeatedly is what an attempt to use this channel for something
        // else looks like.
        builder.HasIndex(e => e.SentAtUtc)
            .HasDatabaseName("ix_settlement_messages_refusals")
            .HasFilter($"outcome <> '{SettlementOutcomes.Accepted}'");

        // The value set SettlementOutcomes.All documents - see SettlementOutcomes.
        builder.ToTable(t =>
            t.HasCheckConstraint("ck_settlement_messages_outcome",
                $"outcome IN ('{string.Join("','", SettlementOutcomes.All)}')"));

        // Restrict, like every other money-side FK in this schema: a message that produced a payment
        // is evidence, and it must not disappear because the payment did. Null is allowed and normal -
        // a refusal produced no payment at all.
        builder.HasOne(e => e.Payment)
            .WithMany(p => p.SettlementMessages)
            .HasForeignKey(e => e.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Money declared as zero is not money, and the parser already refuses it. Stated here as well
        // so a future write path cannot introduce one behind the parser's back.
        builder.ToTable(t =>
            t.HasCheckConstraint("ck_settlement_messages_amount_positive",
                "amount_uah IS NULL OR amount_uah > 0"));
    }
}