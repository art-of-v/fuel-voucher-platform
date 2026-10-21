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
            .IsRequired().HasComment("The setting's name. It IS the key - the row's identity is its name, not a number.");

        builder.Property(e => e.Value)
            .HasColumnName("value")
            .HasMaxLength(4000)
            .IsRequired().HasComment("The value, as text. Every setting is text here, and the one that is a number says so in" +
                    "its own name.");

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired().HasComment("When the setting was last changed.");

        builder.Property(e => e.UpdatedByUserId)
            .HasColumnName("updated_by_user_id").HasComment("Who changed it. NULL when the change came from a job rather than from a person.");
        builder.Property(e => e.UpdatedByUserName)
            .HasColumnName("updated_by_user_name")
            .HasMaxLength(200).HasComment("Their name at the time. A copy, so the record stays readable after a rename.");
    }
}
