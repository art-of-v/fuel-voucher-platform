using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Auth.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id);

        builder.Property(e => e.UserId)
            .IsRequired();

        builder.Property(e => e.Token)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.ExpiresAtUtc)
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        builder.Property(e => e.IsRevoked)
            .IsRequired();

        builder.Property(e => e.RevokedAtUtc);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.Token)
            .IsUnique();

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => new { e.UserId, e.IsRevoked });
    }
}
