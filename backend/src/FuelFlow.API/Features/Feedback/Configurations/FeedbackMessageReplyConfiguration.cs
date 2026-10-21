using FuelFlow.Features.Feedback.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Feedback.Configurations;

internal sealed class FeedbackMessageReplyConfiguration : IEntityTypeConfiguration<FeedbackMessageReply>
{
    public void Configure(EntityTypeBuilder<FeedbackMessageReply> builder)
    {
        builder.ToTable("feedback_message_replies");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.FeedbackMessageId)
            .HasColumnName("feedback_message_id")
            .IsRequired().HasComment("The message this replies to.");

        // No cascade: replies are the audit trail of a conversation, and a row that can be deleted
        // by deleting its parent is not an audit trail. DataRetentionService removes them on the
        // same 180-day schedule as the messages they answer, oldest first.
        builder.HasOne(e => e.FeedbackMessage)
            .WithMany()
            .HasForeignKey(e => e.FeedbackMessageId)
            .OnDelete(DeleteBehavior.Restrict);

        // staff_user_id is a TELEGRAM id, not users.id: the reply is written from the staff group,
        // and its author is whoever is in that chat. It is a bigint for that reason, and it can
        // never carry a foreign key to users - there is no such row to point at. The name is
        // captured beside it, because a Telegram id is not a readable audit trail.
        builder.Property(e => e.StaffUserId)
            .HasColumnName("staff_user_id")
            .IsRequired().HasComment("The Telegram id of the staff member who wrote it.");

        builder.Property(e => e.StaffUserName)
            .HasColumnName("staff_user_name")
            .HasMaxLength(200).HasComment("Their display name as it was at the moment of sending - Telegram ids are not a readable" +
                    "audit trail.");

        builder.Property(e => e.Text)
            .HasColumnName("text")
            .HasMaxLength(2000)
            .IsRequired().HasComment("What was written.");

        builder.Property(e => e.DeliveredMessageId).HasColumnName("delivered_message_id").HasComment("The id the reply got in the customer's chat, or NULL when the delivery failed.");

        builder.Property(e => e.DeliveryError)
            .HasColumnName("delivery_error")
            .HasMaxLength(1000).HasComment("The last delivery failure reason, truncated. NULL on success.");

        builder.Property(e => e.SentAtUtc)
            .HasColumnName("sent_at_utc")
            .IsRequired().HasComment("When it was sent.");

        builder.HasIndex(e => e.FeedbackMessageId);
        builder.HasIndex(e => e.SentAtUtc);
    }
}