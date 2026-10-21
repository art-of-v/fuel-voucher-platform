using FuelFlow.Features.Feedback.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Feedback.Configurations;

internal sealed class FeedbackMessageConfiguration : IEntityTypeConfiguration<FeedbackMessage>
{
    public void Configure(EntityTypeBuilder<FeedbackMessage> builder)
    {
        builder.ToTable("feedback_messages");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");

        // Deliberately no FK to users: the row is written by an anonymous Telegram webhook, and a
        // user who later soft-deletes their account must not take their own feedback with them
        // (the same reasoning as fuel_vouchers.order_id being required but not cascading).
        builder.Property(e => e.UserId).HasColumnName("user_id")
            .HasComment(                "The FuelFlow account this chat was bound to, or NULL for an unbound chat.");

        builder.Property(e => e.TelegramChatId)
            .HasColumnName("telegram_chat_id")
            .IsRequired()
            .HasComment(                "Telegram chat id of the customer's private chat (positive for a 1-on-1 chat).");

        builder.Property(e => e.TelegramMessageId)
            .HasColumnName("telegram_message_id")
            .IsRequired()
            .HasComment(                "The id of this message inside that chat. Unique per chat - the webhook de-dupe key.");

        builder.Property(e => e.Text)
            .HasColumnName("text")
            .HasMaxLength(2000)
            .IsRequired()
            .HasComment(                "What the customer wrote, capped at the configured maximum length.");

        builder.Property(e => e.Verdict)
            .HasColumnName("verdict")
            .HasConversion<string>()
            .HasMaxLength(10)
            .HasComment(                "NULL until the classifier has run. NULL means 'not classified', NOT 'junk' - the two are" +
                    "different facts and the filter has to tell them apart.");

        builder.Property(e => e.Category)
            .HasColumnName("category")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasComment(                "What the classifier says this is about. NULL until it has run.");

        builder.Property(e => e.Severity)
            .HasColumnName("severity")
            .HasConversion<string>()
            .HasMaxLength(10)
            .HasComment(                "How serious the classifier says it is. NULL until it has run.");

        builder.Property(e => e.Summary)
            .HasColumnName("summary")
            .HasMaxLength(300)
            .HasComment(                "The one-sentence restatement the classifier wrote for the staff chat. Never invented.");

        builder.Property(e => e.Language)
            .HasColumnName("language")
            .HasConversion<string>()
            .HasMaxLength(10)
            .HasComment(                "The language the classifier detected. NULL until it has run.");

        builder.Property(e => e.Confidence)
            .HasColumnName("confidence")
            .HasColumnType("numeric(5,4)")
            .HasComment(                "The classifier's confidence, 0..1. Retained even for dropped messages, so a drifting" +
                    "classifier is visible in the admin filter rather than invisible in the discard bin. A" +
                    "decimal, not a double: binary floating point cannot represent 0.1 exactly, and a" +
                    "confidence of 0.6 is a number people read.");

        builder.Property(e => e.ClassifierError)
            .HasColumnName("classifier_error")
            .HasMaxLength(1000)
            .HasComment(                "The last classifier failure, truncated. A row with this set and no verdict is one the" +
                    "team can still read - it is only the automation that failed.");

        builder.Property(e => e.ClassifiedAtUtc).HasColumnName("classified_at_utc")
            .HasComment(                "When the classifier last ran, so a failed row can be retried or aged out.");

        builder.Property(e => e.StaffChatId).HasColumnName("staff_chat_id")
            .HasComment(                "Where this message was posted for the team, and the id it got there under. A staff reply" +
                    "is only relayed when it quotes this id, which is what stops group chatter reaching" +
                    "customers.");
        builder.Property(e => e.StaffMessageId).HasColumnName("staff_message_id")
            .HasComment(                "The id of the staff reply that was relayed.");
        builder.Property(e => e.StaffRepliedAtUtc).HasColumnName("staff_replied_at_utc")
            .HasComment(                "Set once an allowlisted team member's reply was delivered to the customer.");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasComment(                "Where the message stands.");

        builder.Property(e => e.AppVersion)
            .HasColumnName("app_version")
            .HasMaxLength(50)
            .HasComment(                "The client build the message came from - the first thing asked for in a bug report.");

        builder.Property(e => e.Platform)
            .HasColumnName("platform")
            .HasMaxLength(20)
            .HasComment(                "The client platform (ios / android / web).");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasComment(                "When the customer wrote it.");

        // The webhook de-dupe. Telegram re-delivers an update whenever it does not get a 2xx, and a
        // naive insert would put the same complaint in the staff group twice - which reads to the
        // team as two customers reporting the same bug.
        builder.HasIndex(e => new { e.TelegramChatId, e.TelegramMessageId })
            .IsUnique();

        // The admin list's default ordering and the retention sweep's predicate.
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Category);

        // Every column is supplied by the application on insert, so there is no database default:
            // a row with a missing status could not be rendered by the admin filters that switch
            // on it, and a default here would only hide a bug that forgets to set it.

        // Enum columns are stored as strings, so a typo would otherwise be a silent new category.
        // Each value set is pinned, exactly like ck_outbox_event_type.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_feedback_message_verdict",
                "verdict IS NULL OR verdict IN ('Junk', 'Kept')");
            t.HasCheckConstraint("ck_feedback_message_category",
                "category IS NULL OR category IN ('Bug', 'Idea', 'Complaint', 'Ux', 'Question', 'Praise', 'Other')");
            t.HasCheckConstraint("ck_feedback_message_severity",
                "severity IS NULL OR severity IN ('Low', 'Medium', 'High')");
            t.HasCheckConstraint("ck_feedback_message_language",
                "language IS NULL OR language IN ('Uk', 'Ru', 'En', 'Other')");
            t.HasCheckConstraint("ck_feedback_message_status",
                "status IN ('New', 'Triaged', 'Handled', 'Rejected')");
        });

        // --- F-14: the foreign key this column was missing -------------------------------------------------
        // The reference is real and single-parent, so the database enforces it. ON DELETE is stated
        // explicitly rather than left to the default: a schema that says NO ACTION by accident is a
        // schema nobody has thought about.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Postgres does not index a foreign key column on its own, and the parent's delete check and
        // every join down this column scan without one.
        builder.HasIndex(e => e.UserId);
    }
}
