using FuelFlow.Features.Contracts.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Contracts.Configurations;

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("contracts");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(e => e.LegalEntityId)
            .HasColumnName("legal_entity_id")
            .IsRequired();

        builder.Property(e => e.StationId)
            .HasColumnName("station_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        // A signed contract is not owned by the account that signed it, nor by the company or the
        // brand it names. Cascade here used to walk users -> legal_entities -> contracts ->
        // user_contracts and end at a digital signature; Restrict stops it at the first hop.
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Entity)
            .WithMany()
            .HasForeignKey(e => e.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Station)
            .WithMany()
            .HasForeignKey(e => e.StationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.LegalEntityId);
        builder.HasIndex(e => e.StationId);

        // station_id is a natural key on the catalog ("okko"), bounded by CHECK rather than a type
        // change - see StationConfiguration.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_contracts_station_id_length", "length(station_id) <= 100");
        });
    }
}
