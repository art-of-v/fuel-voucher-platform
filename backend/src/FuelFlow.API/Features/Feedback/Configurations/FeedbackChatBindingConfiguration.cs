using FuelFlow.Features.Feedback.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Feedback.Configurations;

internal sealed class FeedbackChatBindingConfiguration : IEntityTypeConfiguration<FeedbackChatBinding>
{
    public void Configure(EntityTypeBuilder<FeedbackChatBinding> builder)
    {
        builder.ToTable("feedback_chat_bindings");

        builder.HasKey(e => e.TelegramChatId);

        builder.Property(e => e.TelegramChatId).HasColumnName("telegram_chat_id");

        // No FK to users, matching feedback_messages: the binding is written by an anonymous
        // webhook and must outlive an account being soft-deleted, because the feedback that
        // customer left is still worth reading afterwards.
        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(e => e.BoundAtUtc).HasColumnName("bound_at_utc").IsRequired();

        builder.HasIndex(e => e.UserId);

        // --- F-14: the foreign key this column was missing -------------------------------------------------
        // The reference is real and single-parent, so the database enforces it. ON DELETE is stated
        // explicitly rather than left to the default: a schema that says NO ACTION by accident is a
        // schema nobody has thought about.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Postgres does not index a foreign key column on its own, and the parent's delete check and
        // every join down this column scan without one.
        builder.HasIndex(e => e.UserId);
    }
}
