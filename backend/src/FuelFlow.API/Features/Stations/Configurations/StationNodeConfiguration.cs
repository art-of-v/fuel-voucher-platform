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
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Address)
            .HasColumnName("address")
            .HasMaxLength(500);

        builder.Property(e => e.Phone)
            .HasColumnName("phone")
            .HasMaxLength(45);

        builder.Property(e => e.City)
            .HasColumnName("city")
            .HasMaxLength(128);

        builder.Property(e => e.StationType)
            .HasColumnName("station_type")
            .HasMaxLength(64);

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

        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(e => e.DeletedAtUtc).HasColumnName("deleted_at_utc");

        builder.HasQueryFilter(e => !e.IsDeleted);

        // Restrict: deleting a brand must never delete every physical location that belonged to
        // it. The catalog is archived via IsDeleted instead; this is the seatbelt for a DELETE
        // that arrives from anywhere else - including a hand-written psql session.
        builder.HasOne<Station>()
            .WithMany()
            .HasForeignKey(e => e.StationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.StationId);

        // Natural key, bounded by CHECK rather than a type change - see StationConfiguration.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_station_nodes_id_length", "length(id) <= 100");
            t.HasCheckConstraint("ck_station_nodes_station_id_length", "length(station_id) <= 100");
        });
    }
}
