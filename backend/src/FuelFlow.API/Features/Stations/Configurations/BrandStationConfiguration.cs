using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Brands.Configurations;

internal sealed class BrandStationConfiguration : IEntityTypeConfiguration<BrandStation>
{
    public void Configure(EntityTypeBuilder<BrandStation> builder)
    {
        builder.ToTable("brand_stations");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasColumnType("text");

        builder.Property(e => e.BrandId)
            .HasColumnName("brand_id")
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
        builder.HasOne<Brand>()
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.BrandId);

        // Natural key, bounded by CHECK rather than a type change - see BrandConfiguration.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_brand_stations_id_length", "length(id) <= 100");
            t.HasCheckConstraint("ck_brand_stations_brand_id_length", "length(brand_id) <= 100");
        });
    }
}
