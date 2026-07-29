using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Stations.Configurations;

internal sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> builder)
    {
        builder.ToTable("stations");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnType("text");

        builder.Property(e => e.Name)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Color)
            .HasColumnType("text")
            .HasDefaultValue("#00ff80")
            .IsRequired();

        builder.Property(e => e.LogoText)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Address)
            .HasColumnType("text");

        builder.Property(e => e.Phone)
            .HasColumnType("text");

        builder.Property(e => e.StationType)
            .HasColumnType("text");

        builder.Property(e => e.Lat)
            .HasColumnType("double precision");

        builder.Property(e => e.Lng)
            .HasColumnType("double precision");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasMany<StationNode>()
            .WithOne()
            .HasForeignKey(e => e.StationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
