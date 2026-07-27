using FuelFlow.Features.Stations.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Stations.Configurations;

internal sealed class PriceChangeAuditConfiguration : IEntityTypeConfiguration<PriceChangeAudit>
{
    public void Configure(EntityTypeBuilder<PriceChangeAudit> builder)
    {
        builder.ToTable("price_change_audit");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.FuelTypeId)
            .HasColumnName("fuel_type_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.OldBasePrice)
            .HasColumnName("old_base_price")
            .IsRequired();

        builder.Property(e => e.NewBasePrice)
            .HasColumnName("new_base_price")
            .IsRequired();

        builder.Property(e => e.OldDiscountPrice)
            .HasColumnName("old_discount_price")
            .IsRequired();

        builder.Property(e => e.NewDiscountPrice)
            .HasColumnName("new_discount_price")
            .IsRequired();

        builder.Property(e => e.ChangedByUserId)
            .HasColumnName("changed_by_user_id")
            .IsRequired();

        builder.Property(e => e.ChangedAtUtc)
            .HasColumnName("changed_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(e => e.FuelTypeId);
        builder.HasIndex(e => e.ChangedAtUtc);
    }
}
