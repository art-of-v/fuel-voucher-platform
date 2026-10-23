using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Redemptions.Configurations;

internal sealed class VoucherRedemptionConfiguration : IEntityTypeConfiguration<VoucherRedemption>
{
    public void Configure(EntityTypeBuilder<VoucherRedemption> builder)
    {
        builder.ToTable("voucher_redemptions");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.VoucherId)
            .HasColumnName("voucher_id")
            .IsRequired()
            .HasComment("The voucher this claim is about.");

        builder.HasOne(e => e.Voucher)
            .WithMany()
            .HasForeignKey(e => e.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasComment("Where the claim stands: reported, confirmed, withdrawn, rejected or reversed.");

        builder.Property(e => e.ReportedLiters)
            .HasColumnName("reported_liters")
            .HasColumnType("numeric(10,2)")
            .IsRequired()
            .HasComment("The litres claimed by the reporter or statement. Normally the voucher's own litres.");

        builder.Property(e => e.RedeemedAtUtc)
            .HasColumnName("redeemed_at_utc")
            .IsRequired()
            .HasComment("When the fuel was actually taken, as far as the evidence says.");

        builder.Property(e => e.ReportedAtUtc)
            .HasColumnName("reported_at_utc")
            .IsRequired()
            .HasComment("When this claim entered the system.");

        builder.Property(e => e.ReportedByUserId)
            .HasColumnName("reported_by_user_id")
            .HasComment("The customer who self-reported it. NULL means a supplier statement raised it.");

        builder.Property(e => e.SupplierReference)
            .HasColumnName("supplier_reference")
            .HasMaxLength(100)
            .HasComment("The supplier's line reference, when the claim came from a statement.");

        builder.Property(e => e.ConfirmedAtUtc)
            .HasColumnName("confirmed_at_utc")
            .HasComment("When the evidence was accepted.");

        builder.Property(e => e.ConfirmedByUserId)
            .HasColumnName("confirmed_by_user_id")
            .HasComment("The operator who accepted the evidence. Bare uuid, no foreign key.");

        builder.Property(e => e.ClosedAtUtc)
            .HasColumnName("closed_at_utc")
            .HasComment("When the claim was withdrawn, rejected or reversed.");

        builder.Property(e => e.ClosedByUserId)
            .HasColumnName("closed_by_user_id")
            .HasComment("The actor who closed the claim. Bare uuid, no foreign key.");

        builder.Property(e => e.ResolutionNote)
            .HasColumnName("resolution_note")
            .HasMaxLength(500)
            .HasComment("Why the operator closed it, in their own words.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        // One active claim per voucher. Closed rows stay as history, so a rejected report does not
        // prevent a later statement from opening a new claim on the same paper.
        builder.HasIndex(e => e.VoucherId)
            .IsUnique()
            .HasFilter("status IN ('Reported', 'Confirmed')")
            .HasDatabaseName("ux_voucher_redemptions_active_voucher");

        builder.HasIndex(e => e.SupplierReference)
            .IsUnique()
            .HasFilter("supplier_reference IS NOT NULL")
            .HasDatabaseName("ux_voucher_redemptions_supplier_reference");

        builder.HasIndex(e => new { e.Status, e.ReportedAtUtc });
        builder.HasIndex(e => new { e.ReportedByUserId, e.Status });

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_voucher_redemptions_status", """
                status IN ('Reported', 'Confirmed', 'Withdrawn', 'Rejected', 'Reversed')
                """);
            t.HasCheckConstraint("ck_voucher_redemptions_liters_positive", "reported_liters > 0");
            t.HasCheckConstraint(
                "ck_voucher_redemptions_confirmed_shape",
                "status <> 'Confirmed' OR (confirmed_at_utc IS NOT NULL AND confirmed_by_user_id IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_voucher_redemptions_closed_shape",
                "status NOT IN ('Withdrawn', 'Rejected', 'Reversed') "
                + "OR (closed_at_utc IS NOT NULL AND closed_by_user_id IS NOT NULL)");
        });
    }
}
