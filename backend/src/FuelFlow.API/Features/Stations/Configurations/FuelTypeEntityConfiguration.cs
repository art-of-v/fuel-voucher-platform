using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Stations.Configurations;

internal sealed class FuelTypeEntityConfiguration : IEntityTypeConfiguration<FuelTypeEntity>
{
    public void Configure(EntityTypeBuilder<FuelTypeEntity> builder)
    {
        builder.ToTable("fuel_types");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(e => e.DeletedAtUtc).HasColumnName("deleted_at_utc");

        // fuel_vouchers.fuel_type_id cascades from here, so archiving rather than removing is what
        // keeps paid-for vouchers in existence (#217).
        builder.HasQueryFilter(e => !e.IsDeleted);


        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasColumnType("text");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.StationId)
            .HasColumnName("station_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.BasePrice)
            .HasColumnName("base_price")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(e => e.DiscountPrice)
            .HasColumnName("discount_price")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(e => e.AllowBelowCost)
            .HasColumnName("allow_below_cost")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(e => e.Name);
        builder.HasIndex(e => e.StationId);
        builder.HasIndex(e => new { e.StationId, e.Name });

        // Natural key ("okko-95"), deliberately not a UUID. Bounded by CHECK rather than by a type
        // change - see StationConfiguration for why converting a natural key is not worth the
        // refactor, and what the CHECK buys instead.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_fuel_types_id_length", "length(id) <= 100");
            t.HasCheckConstraint("ck_fuel_types_name_length", "length(name) <= 200");
            t.HasCheckConstraint("ck_fuel_types_station_id_length", "length(station_id) <= 100");

            // A negative discount price means the customer is paid to take the fuel, which is not a
            // discount policy anyone has agreed to. The only guard today is the application-level
            // below-cost opt-in (BrandsController), which is a business decision rather than a
            // constraint, so a hand-written PUT or a future code path can still land here.
            t.HasCheckConstraint("ck_fuel_types_prices_non_negative", "base_price >= 0 AND discount_price >= 0");
        });
    }
}
