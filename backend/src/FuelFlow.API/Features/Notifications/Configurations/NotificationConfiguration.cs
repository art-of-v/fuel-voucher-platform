using FuelFlow.Features.Notifications.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Notifications.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(e => e.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Message)
            .HasColumnName("message")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(e => e.IsRead)
            .HasColumnName("is_read")
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The real read is `WHERE user_id = @id ORDER BY created_at_utc DESC`, which neither index
        // could serve: (user_id) does not order, and (user_id, is_read) leads with the wrong
        // column. This one replaces the bare user_id index, which is its leftmost prefix.
        builder.HasIndex(e => new { e.UserId, e.CreatedAtUtc })
            .IsDescending(false, true);
        builder.HasIndex(e => new { e.UserId, e.IsRead });
    }
}
