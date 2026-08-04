using FuelFlow.Features.Settings.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Settings.Configurations;

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings");

        builder.HasKey(e => e.Key);

        builder.Property(e => e.Key)
            .HasColumnName("key")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Value)
            .HasColumnName("value")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.Property(e => e.UpdatedByUserId)
            .HasColumnName("updated_by_user_id");

        builder.Property(e => e.UpdatedByUserName)
            .HasColumnName("updated_by_user_name")
            .HasMaxLength(200);
    }
}
