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

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .IsRequired();

        builder.Property(e => e.DeletedAtUtc)
            .HasColumnName("deleted_at_utc");

        builder.HasIndex(e => e.LegalEntityId);

        // One live membership per worker. A fired worker leaves a soft-deleted row behind, and the
        // unique index would then refuse to let the same person be re-invited - so the index covers
        // only the rows that still count. Same shape as User.referral_code / phone_number.
        builder.HasIndex(e => e.WorkerUserId)
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_company_members_worker_user_id");

        builder.HasQueryFilter(e => !e.IsDeleted);

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
