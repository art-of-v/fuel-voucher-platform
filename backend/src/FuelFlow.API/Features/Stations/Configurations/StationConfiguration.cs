using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Stations.Configurations;

internal sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> builder)
    {
        builder.ToTable("stations");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasColumnType("text");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Color)
            .HasColumnName("color")
            .HasColumnType("text")
            .HasDefaultValue("#00ff80")
            .IsRequired();

        builder.Property(e => e.LogoText)
            .HasColumnName("logo_text")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Address)
            .HasColumnName("address")
            .HasColumnType("text");

        builder.Property(e => e.Phone)
            .HasColumnName("phone")
            .HasColumnType("text");

        builder.Property(e => e.StationType)
            .HasColumnName("station_type")
            .HasColumnType("text");

        builder.Property(e => e.Lat)
            .HasColumnName("lat")
            .HasColumnType("text");

        builder.Property(e => e.Lng)
            .HasColumnName("lng")
            .HasColumnType("text");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasMany<StationNode>()
            .WithOne()
            .HasForeignKey(e => e.StationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
