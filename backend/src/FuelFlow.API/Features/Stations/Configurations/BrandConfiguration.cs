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
            .HasColumnType("text");

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

        builder.Property(e => e.Address)
            .HasColumnName("address")
            .HasMaxLength(500);

        builder.Property(e => e.Phone)
            .HasColumnName("phone")
            .HasMaxLength(45);

        builder.Property(e => e.StationType)
            .HasColumnName("station_type")
            .HasMaxLength(64);

        builder.Property(e => e.Lat)
            .HasColumnName("lat")
            .HasColumnType("double precision");

        builder.Property(e => e.Lng)
            .HasColumnName("lng")
            .HasColumnType("double precision");

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
        builder.HasIndex(e => e.StationType);

        // The catalog keys are natural strings ("okko", "wog"), not UUIDs - readable in a row
        // and meaningful to the operator. That is a deliberate choice, and this constraint is the
        // only thing bounding them: `id` stays `text` because converting a primary key's type means
        // dropping and recreating every foreign key that points at it, across seven tables, and
        // the gain would be cosmetic. A CHECK gets the guarantee that actually matters - a slug
        // cannot silently become a paragraph - for the cost of one line and zero refactoring.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_brands_id_length", "length(id) <= 100");
            t.HasCheckConstraint("ck_brands_name_length", "length(name) <= 200");
        });

        builder.HasMany<BrandStation>()
            .WithOne()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
