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
    }
}
