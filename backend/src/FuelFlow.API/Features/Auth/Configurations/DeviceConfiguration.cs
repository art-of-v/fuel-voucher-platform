using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Auth.Configurations;

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(e => e.DeviceId)
            .HasColumnName("device_id")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(e => e.PublicKey)
            .HasColumnName("public_key")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.DeviceModel)
            .HasColumnName("device_model")
            .HasMaxLength(255);

        builder.Property(e => e.OsVersion)
            .HasColumnName("os_version")
            .HasMaxLength(255);

        builder.Property(e => e.AppVersion)
            .HasColumnName("app_version")
            .HasMaxLength(255);

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.LastSeenAt)
            .HasColumnName("last_seen_at")
            .IsRequired();

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.DeviceId)
            .IsUnique();

        builder.HasIndex(e => e.UserId);
    }
}
