using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Brands.Configurations;

internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("brands");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasColumnType("uuid");

        // The natural key, demoted from primary key to a unique column (planning D-21). It is still
        // the name an importer, an invoice and an operator writes, so it stays bounded rather than
        // becoming free text.
        builder.Property(e => e.Slug)
            .HasColumnName("slug")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(e => e.Slug).IsUnique();

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        // A hex colour, e.g. the seeded "#00ff80". 16 leaves room for rgba()/8-digit hex without
        // accepting a paragraph into a colour.
        builder.Property(e => e.Color)
            .HasColumnName("color")
            .HasMaxLength(16)
            .HasDefaultValue("#00ff80")
            .IsRequired();

        // A brand wordmark, not artwork: the seeded rows carry "OKKO", "WOG", "UPG".
        builder.Property(e => e.LogoText)
            .HasColumnName("logo_text")
            .HasMaxLength(64)
            .IsRequired();

        // Everything that describes a place - address, phone, station_type, lat, lng - belongs to a
        // row in `brand_stations`, not here. A network is not somewhere you can stand: OKKO has twenty
        // of those and no single one of them is "the OKKO address". The columns sat here all NULL, and
        // `IX_brands_station_type` indexed four NULLs.

        builder.Property(e => e.SortOrder)
            .HasColumnName("sort_order")
            .HasColumnType("integer")
            .HasDefaultValue(999)
            .IsRequired();

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

        // Archived entries leave the catalog but stay reachable for anything historical that points
        // at them - a voucher, a contract, a report.
        builder.HasQueryFilter(e => !e.IsDeleted);

        builder.HasIndex(e => e.Name);
        // The natural key is a column of bounded length rather than a type of its own: it is what an
        // importer resolves a PDF row by, so it must stay readable, and a CHECK is enough to stop it
        // becoming a paragraph.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_brands_slug_length", "length(slug) <= 100");
            t.HasCheckConstraint("ck_brands_name_length", "length(name) <= 200");
        });

        // The stations relation is configured once, on BrandStationConfiguration, where the foreign key
        // and the Restrict live. Declaring it here as well used to be harmless — with no navigation on
        // either side EF folded the two into one relationship — and stopped being harmless the moment
        // BrandStation.Brand existed: EF then saw two configurations of the same relationship and
        // invented a second foreign key, `brand_stations."BrandId1"`, to satisfy one of them. Two
        // configurations, one FK, is the shape that produces a phantom column.
    }
}
