using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Auth.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(50)
            .IsRequired()
            .HasComment(                "The role's name, and the value the application matches on.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasComment(                "When the role was created.");

        builder.Property(e => e.Level)
            .HasColumnName("level")
            .HasComment(                "The role's rank: User 0, QA 5, Manager 10, Admin 50, ProductOwner 100. The hierarchy is" +
                    "the number, so a role's place in it is arithmetic rather than a table of who outranks" +
                    "whom.");

        builder.Property(e => e.IsDefault)
            .HasColumnName("is_default");

        builder.HasIndex(e => e.Name)
            .IsUnique();
    }
}
