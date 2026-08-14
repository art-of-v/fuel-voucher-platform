using FuelFlow.Features.Company.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Company.Configurations;

internal sealed class CompanyInvitationConfiguration : IEntityTypeConfiguration<CompanyInvitation>
{
    public void Configure(EntityTypeBuilder<CompanyInvitation> builder)
    {
        builder.ToTable("company_invitations");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.LegalEntityId)
            .HasColumnName("legal_entity_id")
            .IsRequired();

        builder.Property(e => e.OwnerUserId)
            .HasColumnName("owner_user_id")
            .IsRequired();

        builder.Property(e => e.WorkerPhoneNumber)
            .HasColumnName("worker_phone_number")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.WorkerUserId)
            .HasColumnName("worker_user_id")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.HasIndex(e => e.LegalEntityId);
        builder.HasIndex(e => e.OwnerUserId);
        builder.HasIndex(e => e.WorkerUserId);
        builder.HasIndex(e => new { e.WorkerUserId, e.Status });

        builder.HasOne(e => e.LegalEntity)
            .WithMany()
            .HasForeignKey(e => e.LegalEntityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.OwnerUser)
            .WithMany()
            .HasForeignKey(e => e.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.WorkerUser)
            .WithMany()
            .HasForeignKey(e => e.WorkerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
