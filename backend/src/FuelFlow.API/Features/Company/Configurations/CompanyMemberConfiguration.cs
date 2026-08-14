using FuelFlow.Features.Company.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Company.Configurations;

internal sealed class CompanyMemberConfiguration : IEntityTypeConfiguration<CompanyMember>
{
    public void Configure(EntityTypeBuilder<CompanyMember> builder)
    {
        builder.ToTable("company_members");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.LegalEntityId)
            .HasColumnName("legal_entity_id")
            .IsRequired();

        builder.Property(e => e.WorkerUserId)
            .HasColumnName("worker_user_id")
            .IsRequired();

        builder.Property(e => e.JoinedAtUtc)
            .HasColumnName("joined_at_utc")
            .IsRequired();

        builder.HasIndex(e => e.LegalEntityId);
        builder.HasIndex(e => e.WorkerUserId)
            .IsUnique();

        builder.HasOne(e => e.LegalEntity)
            .WithMany()
            .HasForeignKey(e => e.LegalEntityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.WorkerUser)
            .WithMany()
            .HasForeignKey(e => e.WorkerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
