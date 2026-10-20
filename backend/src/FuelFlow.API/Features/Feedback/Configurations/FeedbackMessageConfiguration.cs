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
        builder.Property(e => e.UserId).HasColumnName("user_id");

        builder.Property(e => e.TelegramChatId)
            .HasColumnName("telegram_chat_id")
            .IsRequired();

        builder.Property(e => e.TelegramMessageId)
            .HasColumnName("telegram_message_id")
            .IsRequired();

        builder.Property(e => e.Text)
            .HasColumnName("text")
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(e => e.Verdict)
            .HasColumnName("verdict")
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(e => e.Category)
            .HasColumnName("category")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.Severity)
            .HasColumnName("severity")
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(e => e.Summary)
            .HasColumnName("summary")
            .HasMaxLength(300);

        builder.Property(e => e.Language)
            .HasColumnName("language")
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(e => e.Confidence)
            .HasColumnName("confidence")
            .HasColumnType("numeric(5,4)");

        builder.Property(e => e.ClassifierError)
            .HasColumnName("classifier_error")
            .HasMaxLength(1000);

        builder.Property(e => e.ClassifiedAtUtc).HasColumnName("classified_at_utc");

        builder.Property(e => e.StaffChatId).HasColumnName("staff_chat_id");
        builder.Property(e => e.StaffMessageId).HasColumnName("staff_message_id");
        builder.Property(e => e.StaffRepliedAtUtc).HasColumnName("staff_replied_at_utc");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.AppVersion)
            .HasColumnName("app_version")
            .HasMaxLength(50);

        builder.Property(e => e.Platform)
            .HasColumnName("platform")
            .HasMaxLength(20);

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

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
