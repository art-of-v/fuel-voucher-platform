using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Stations.Configurations;

/// <summary>
/// <c>fuel_offer_volumes</c> — the volumes an offer is sold in. Planning #318, finding F-33.
/// </summary>
internal sealed class FuelOfferVolumeConfiguration : IEntityTypeConfiguration<FuelOfferVolume>
{
    public void Configure(EntityTypeBuilder<FuelOfferVolume> builder)
    {
        builder.ToTable("fuel_offer_volumes");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("text").IsRequired();
        builder.Property(e => e.OfferId).HasColumnName("offer_id").HasColumnType("text").IsRequired();

        builder.Property(e => e.Liters).HasColumnName("liters").HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(e => e.PackagePrice).HasColumnName("package_price").HasColumnType("numeric(12,2)").IsRequired();

        builder.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");
        builder.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamptz");

        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(e => e.DeletedAtUtc).HasColumnName("deleted_at_utc");

        // Same filter as FuelPackage.IsDeleted has, and deliberately so: every read path that today relies
        // on the package's filter would silently change meaning when PR B points it at volumes, and
        // an archived volume would quietly reappear in the catalogue listing. Reports that need an
        // archived volume say so with IgnoreQueryFilters().
        builder.HasQueryFilter(e => !e.IsDeleted);

        // THE guarantee of this split. One volume per offer, enforced by the database, so the
        // ambiguous "(station, fuel, litres) picks an arbitrary row" lookup that planning #315 had to
        // resolve with an ORDER BY cannot come back through the new tables.
        builder.HasIndex(e => new { e.OfferId, e.Liters })
            .IsUnique()
            .HasDatabaseName("ux_fuel_offer_volumes_offer_liters");

        // The catalog listing: live volumes of a live offer, ordered by size.
        builder.HasIndex(e => new { e.IsDeleted, e.Liters });

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_fuel_offer_volumes_id_length", "length(id) <= 100");

            // The offer id is the fuel type id plus the pool suffix - the same 103 OfferKeys derives -
            // so this is not an independent guess about how long a key may be.
            t.HasCheckConstraint("ck_fuel_offer_volumes_offer_id_length", "length(offer_id) <= 103");

            // Carried over from ck_fuel_packages_liters_positive (#314): PUT .../nominals binds
            // List<int> off the request body, so a nominal of -10 was reachable from the admin API.
            t.HasCheckConstraint("ck_fuel_offer_volumes_liters_positive", "liters > 0");

            // And from ck_fuel_packages_price_non_negative (#314), same reachability.
            t.HasCheckConstraint("ck_fuel_offer_volumes_package_price_non_negative", "package_price >= 0");
        });
    }
}