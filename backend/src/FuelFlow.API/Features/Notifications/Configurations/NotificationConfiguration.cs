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
            .IsRequired()
            .HasComment(                "The account this notification is for.");

        builder.Property(e => e.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired()
            .HasComment(                "The notification's headline.");

        builder.Property(e => e.Message)
            .HasColumnName("message")
            .HasMaxLength(1000)
            .IsRequired()
            .HasComment(                "The notification's body.");

        builder.Property(e => e.IsRead)
            .HasColumnName("is_read")
            .IsRequired()
            .HasComment(                "Whether the customer has read it.");

        builder.Property(e => e.Type)
            .HasColumnName("type")
            .HasMaxLength(32)
            .HasDefaultValue(NotificationTypes.Generic)
            .IsRequired()
            .HasComment(                "What the notification is about - the routing key the client reads.");

        builder.Property(e => e.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .HasComment(                "What a client needs to open what the notification is about, as JSON. NULL when there is nothing to open.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasComment(                "When it was created.");

        // The default used to be '-infinity', and it was not chosen: it is what Npgsql writes for
        // DateTime.MinValue, which is what EF's convention put into the migrations that created this
        // column. -infinity sorts before every real timestamp and serialises badly, and it is a value
        // no application means.
        //
        // now() is the honest fallback for a raw INSERT that does not state the timestamp, and
        // ValueGeneratedNever keeps the application in charge: it still sends its own value on every
        // insert, exactly as it did before, so nothing about the application's behaviour changes. The
        // default is a safety net for SQL that does not come from EF.
        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasDefaultValueSql("now()")
            .ValueGeneratedNever()
            .IsRequired()
            .HasComment(                "When it last changed.");

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

        // Retention prunes `IsRead && CreatedAtUtc < cutoff`, which leads with a column neither
        // index above can serve - both start at UserId, and the prune has no UserId to filter on.
        // So it ran as a Seq Scan (31.8 ms at 200k rows, 47k matched). This pair matches the
        // predicate's own order and turns it into a bitmap scan (0.114 ms). Read-only
        // notifications are kept indefinitely, so IsRead has to lead, not trail.
        builder.HasIndex(e => new { e.IsRead, e.CreatedAtUtc });

        // The value set NotificationTypes defines. A typo in a routing key is the worst shape a
        // notification can take: the row reads as ordinary, and the one thing it was for never
        // happens. The same reasoning as ck_outbox_event_type.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_notification_type", """
                type IN ('generic', 'order_fulfilled', 'voucher_issued')
                """);
        });
    }
}
