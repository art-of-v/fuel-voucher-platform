using FuelFlow.Features.Company.SharedModels;
using FuelFlow.SharedKernel.Database;
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
            .IsRequired()
            .HasComment(                "The company.");

        builder.Property(e => e.WorkerUserId)
            .HasColumnName("worker_user_id")
            .IsRequired()
            .HasComment(                "The person.");

        builder.Property(e => e.JoinedAtUtc)
            .HasColumnName("joined_at_utc")
            .IsRequired()
            .HasComment(                "When they joined.");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .IsRequired()
            .HasComment(                "Firing a worker sets this rather than removing the row. Employment history IS history:" +
                    "the owner policy keeps anything that affects it forever, and 'this person worked here" +
                    "until March' is a fact about the company, not garbage a cleanup job may collect. The" +
                    "!IsDeleted query filter keeps it out of every membership read.");

        builder.Property(e => e.DeletedAtUtc)
            .HasColumnName("deleted_at_utc")
            .HasComment(                "When the membership was soft-deleted, or NULL while it stands.");

        builder.HasIndex(e => e.LegalEntityId);

        // One live membership per (company, worker) - #150 W1. A person may work for N companies, so
        // the uniqueness that matters is "not twice in the SAME company", not "not twice anywhere":
        // the old global unique index on worker_user_id made a second company impossible, which is
        // the opposite of the decision. The filter still covers only live rows, because a fired
        // worker leaves a soft-deleted row behind and must still be re-invitable to that company.
        builder.HasIndex(e => new { e.LegalEntityId, e.WorkerUserId })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_company_members_legal_entity_worker");

        // Deliberately NOT unique, and kept because the composite index above cannot serve it:
        // that one leads with legal_entity_id, while "which companies does this person work for"
        // (GetMyMemberships, the worker context in the switcher) filters on worker_user_id alone.
        // Without this it is a sequential scan of the whole membership table.
        builder.HasIndex(e => e.WorkerUserId)
            .HasDatabaseName("ix_company_members_worker_user_id");

        builder.HasQueryFilter(QueryFilterKeys.SoftDelete, e => !e.IsDeleted);

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
