using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Configurations;

internal sealed class PurchaseBatchConfiguration : IEntityTypeConfiguration<PurchaseBatch>
{
    public void Configure(EntityTypeBuilder<PurchaseBatch> builder)
    {
        builder.ToTable("purchase_batches");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.ImportJobId)
            .HasColumnName("import_job_id")
            .IsRequired();

        builder.Property(e => e.FuelTypeId)
            .HasColumnName("fuel_type_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Provider)
            .HasColumnName("provider")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.CostPerLiter)
            .HasColumnName("cost_per_liter")
            .HasColumnType("numeric(10,4)")
            .IsRequired();

        builder.Property(e => e.EnteredByUserId)
            .HasColumnName("entered_by_user_id");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // One cost rate per (import × fuel). Upserts key on this pair.
        builder.HasIndex(e => new { e.ImportJobId, e.FuelTypeId })
            .IsUnique();

        // Blended recompute joins the fuel's whole in-stock pool to batch costs on FuelTypeId.
        builder.HasIndex(e => e.FuelTypeId);
    }
}
