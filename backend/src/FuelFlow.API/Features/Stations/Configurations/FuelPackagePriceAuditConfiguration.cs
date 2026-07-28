using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Stations.Configurations;

internal sealed class FuelPackagePriceAuditConfiguration : IEntityTypeConfiguration<FuelPackagePriceAudit>
{
    public void Configure(EntityTypeBuilder<FuelPackagePriceAudit> builder)
    {
        builder.ToTable("fuel_package_price_audit");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.PackageId)
            .HasColumnName("package_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.FuelName)
            .HasColumnName("fuel_name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.OldSupplierPricePerLiter)
            .HasColumnName("old_supplier_price_per_liter")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.NewSupplierPricePerLiter)
            .HasColumnName("new_supplier_price_per_liter")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.OldMarginUahPerLiter)
            .HasColumnName("old_margin_uah_per_liter")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.NewMarginUahPerLiter)
            .HasColumnName("new_margin_uah_per_liter")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.OldMarginPercent)
            .HasColumnName("old_margin_percent")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.NewMarginPercent)
            .HasColumnName("new_margin_percent")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.OldFinalPricePerLiter)
            .HasColumnName("old_final_price_per_liter")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.NewFinalPricePerLiter)
            .HasColumnName("new_final_price_per_liter")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.ChangedByUserId)
            .HasColumnName("changed_by_user_id")
            .IsRequired();

        builder.Property(e => e.ChangedAtUtc)
            .HasColumnName("changed_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(e => e.PackageId);
        builder.HasIndex(e => e.ChangedAtUtc);
    }
}
