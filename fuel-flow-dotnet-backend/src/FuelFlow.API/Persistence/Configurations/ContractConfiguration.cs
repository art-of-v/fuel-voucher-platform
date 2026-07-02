using FuelFlow.Features.Contracts.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Persistence.Configurations;

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

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Entity)
            .WithMany()
            .HasForeignKey(e => e.LegalEntityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Station)
            .WithMany()
            .HasForeignKey(e => e.StationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
