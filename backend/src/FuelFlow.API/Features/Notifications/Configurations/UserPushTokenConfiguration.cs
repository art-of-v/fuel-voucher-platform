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
            .IsRequired();

        builder.Property(e => e.Token)
            .HasColumnName("token")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(e => e.Platform)
            .HasColumnName("platform")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.DeviceId)
            .HasColumnName("device_id")
            .HasMaxLength(255);

        builder.Property(e => e.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.Property(e => e.LastSeenAtUtc)
            .HasColumnName("last_seen_at_utc")
            .IsRequired();

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.Token)
            .IsUnique();

        builder.HasIndex(e => e.UserId);
    }
}
