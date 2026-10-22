using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Stations.Configurations;

internal sealed class FuelNominalConfiguration : IEntityTypeConfiguration<FuelNominal>
{
    public void Configure(EntityTypeBuilder<FuelNominal> builder)
    {
        builder.ToTable("fuel_nominals");

        // The key is the fact: one fuel, one pool, one size, one row. No surrogate - see the type.
        builder.HasKey(e => new { e.FuelTypeId, e.DataKind, e.Liters });

           builder.Property(e => e.FuelTypeId)
               .HasColumnName("fuel_type_id")
               .HasColumnType("uuid")
               .IsRequired();

        builder.Property(e => e.DataKind)
            .HasColumnName("data_kind")
            // varchar(10), like every other data_kind column in the schema. The value is one of
            // two words, and the CHECK beside it says so.
            .HasMaxLength(10)
            .IsRequired();

        // A named value, not a boolean: a rehearsal row that cannot be called anything else cannot be
        // published as live by a caller who guessed wrong, which is how two 1 UAH rows reached
        // customers before the pool was tracked at all.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_fuel_nominals_data_kind", "data_kind IN ('real', 'qa')"));

        builder.Property(e => e.Liters)
            .HasColumnName("liters")
            .HasColumnType("numeric(10,2)");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<FuelTypeEntity>()
            .WithMany()
            .HasForeignKey(e => e.FuelTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        // A negative size is not a small mistake: the price is per litre times this, so -10 would price a
        // nominal backwards and publish it. The application rejects it with a message naming the value;
        // this is the database agreeing.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_fuel_nominals_liters_positive", "liters > 0"));
    }
}