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
            .HasColumnType("uuid");

        builder.Property(e => e.ExternalId)
            .HasColumnName("external_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.BrandId)
            .HasColumnName("brand_id")
            .HasColumnType("uuid")
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
        builder.HasOne(e => e.Brand)
            .WithMany(b => b.Stations)
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.BrandId);

        // The re-import's identity. Per brand rather than global because the same external id at two
        // networks is two different АЗК, and a source file that carries its own ids should not have
        // to make them unique across every network we sell.
        builder.HasIndex(e => new { e.BrandId, e.ExternalId })
            .IsUnique()
            .HasDatabaseName("ux_brand_stations_brand_external");

        // The sort key, so that a station list reads in the order a person would write it.
        //
        // The names carry a number - "OKKO №01", "OKKO №10", "OKKO №100" - and a byte sort puts
        // them in the order "№01", "№09", "№1", "№10", "№100", "№11", because "1" sorts before
        // "2". The catalogue is 1,322 stations and 598 of the distinct names carry a number, so this
        // is most of the list rather than an edge case.
        //
        // It is a GENERATED column rather than something the application sorts, for one reason: the
        // admin list is paged, and Skip/Take run in the database. A sort that happened after the
        // page was cut would put the same stations on two pages and leave others on none.
        //
        // A shadow property, so the column does not appear in the JSON the admin API returns. It is
        // a sort key, not a field anybody reads.
        builder.Property<string>("NameSortKey")
            .HasColumnName("name_sort_key")
            .HasComputedColumnSql("fuelflow_natural_sort_key(name)", stored: true);

        builder.HasIndex("NameSortKey").HasDatabaseName("ix_brand_stations_name_sort_key");
    }
}
