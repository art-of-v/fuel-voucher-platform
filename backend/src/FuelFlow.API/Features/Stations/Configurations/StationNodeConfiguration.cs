using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Stations.Configurations;

internal sealed class StationNodeConfiguration : IEntityTypeConfiguration<StationNode>
{
    public void Configure(EntityTypeBuilder<StationNode> builder)
    {
        builder.ToTable("station_nodes");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasColumnType("text");

        builder.Property(e => e.StationId)
            .HasColumnName("station_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Address)
            .HasColumnName("address")
            .HasColumnType("text");

        builder.Property(e => e.Phone)
            .HasColumnName("phone")
            .HasColumnType("text");

        builder.Property(e => e.City)
            .HasColumnName("city")
            .HasColumnType("text");

        builder.Property(e => e.StationType)
            .HasColumnName("station_type")
            .HasColumnType("text");

        builder.Property(e => e.Lat)
            .HasColumnName("lat")
            .HasColumnType("double precision");

        builder.Property(e => e.Lng)
            .HasColumnName("lng")
            .HasColumnType("double precision");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(e => e.StationId);
    }
}
