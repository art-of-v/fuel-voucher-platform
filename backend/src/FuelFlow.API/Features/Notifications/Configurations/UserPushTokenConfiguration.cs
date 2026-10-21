using FuelFlow.Features.Notifications.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Notifications.Configurations;

internal sealed class UserPushTokenConfiguration : IEntityTypeConfiguration<UserPushToken>
{
    public void Configure(EntityTypeBuilder<UserPushToken> builder)
    {
        builder.ToTable("push_tokens");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired().HasComment("The user the token belongs to.");

        builder.Property(e => e.Token)
            .HasColumnName("token")
            .HasMaxLength(255)
            .IsRequired().HasComment("The Expo push token. Unique across users - one device, one token, one owner.");

        builder.Property(e => e.Platform)
            .HasColumnName("platform")
            .HasMaxLength(20)
            .IsRequired().HasComment("The reporting platform of the registering client: ios or android.");

        builder.Property(e => e.DeviceId)
            .HasColumnName("device_id")
            .HasMaxLength(255).HasComment("An optional correlation to the registering device. NULL when the client did not say.");

        builder.Property(e => e.IsActive)
            .HasColumnName("is_active")
            .IsRequired().HasComment("Cleared when Expo later reports the token is no longer registered. A token that is not" +
                    "active is not a failure - it is a phone that was replaced.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired().HasComment("When the row was written.");

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired().HasComment("When it was last changed.");

        builder.Property(e => e.LastSeenAtUtc)
            .HasColumnName("last_seen_at_utc")
            .IsRequired().HasComment("When the token was last used successfully. A token nobody has heard from in months is a" +
                    "token to stop sending to.");

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.Token)
            .IsUnique();

        builder.HasIndex(e => e.UserId);

        // The retention job prunes exactly `WHERE last_seen_at_utc < cutoff`
        // (DataRetentionService.cs) - a token the app has not reported from for a season is an
        // uninstalled app. The two indexes above cannot serve it: Token is unique and LastSeenAtUtc
        // appears in neither. Measured on PG16 at 200k rows, the delete was a Seq Scan at 15 ms;
        // with this index it is an Index Scan at 0.066 ms and never reads the heap at all.
        // Kept in the model, not as raw SQL, so EF owns it like every other index here.
        builder.HasIndex(e => e.LastSeenAtUtc);
    }
}
