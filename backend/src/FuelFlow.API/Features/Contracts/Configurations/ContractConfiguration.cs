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

        builder.Property(e => e.Id);

        builder.Property(e => e.UserId)
            .IsRequired();

        builder.Property(e => e.LegalEntityId)
            .IsRequired();

        builder.Property(e => e.StationId)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
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

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.LegalEntityId);
        builder.HasIndex(e => e.StationId);
    }
}
