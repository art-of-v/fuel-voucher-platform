using FuelFlow.Features.Contracts.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Persistence.Configurations;

internal sealed class UserContractConfiguration : IEntityTypeConfiguration<UserContract>
{
    public void Configure(EntityTypeBuilder<UserContract> builder)
    {
        builder.ToTable("user_contracts");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(e => e.ContractId)
            .HasColumnName("contract_id")
            .IsRequired();

        builder.Property(e => e.SignedAtUtc)
            .HasColumnName("signed_at_utc")
            .IsRequired();

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Contract)
            .WithMany()
            .HasForeignKey(e => e.ContractId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
