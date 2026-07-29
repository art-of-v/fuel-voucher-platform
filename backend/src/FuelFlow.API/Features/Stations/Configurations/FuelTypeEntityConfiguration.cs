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

        builder.Property(e => e.Id)
            .HasColumnType("text");

        builder.Property(e => e.Name)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.StationId)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.BasePrice)
            .IsRequired();

        builder.Property(e => e.DiscountPrice)
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(e => e.StationId);
    }
}
