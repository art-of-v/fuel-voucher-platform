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

        builder.Property(e => e.Id);

        builder.Property(e => e.UserId)
            .IsRequired();

        builder.Property(e => e.DeviceId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(e => e.PublicKey)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.DeviceModel)
            .HasMaxLength(255);

        builder.Property(e => e.OsVersion)
            .HasMaxLength(255);

        builder.Property(e => e.AppVersion)
            .HasMaxLength(255);

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.LastSeenAt)
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
