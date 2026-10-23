using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Redemptions.Configurations;

internal sealed class RedemptionStatementRowConfiguration : IEntityTypeConfiguration<RedemptionStatementRow>
{
    public void Configure(EntityTypeBuilder<RedemptionStatementRow> builder)
    {
        builder.ToTable("redemption_statement_rows");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.ExternalId)
            .HasColumnName("external_id")
            .HasMaxLength(100)
            .IsRequired()
            .HasComment("The supplier's stable line id. Unique, so a re-import is idempotent.");

        builder.Property(e => e.VoucherNumber)
            .HasColumnName("voucher_number")
            .HasMaxLength(100)
            .IsRequired()
            .HasComment("The voucher number as the statement printed it.");

        builder.Property(e => e.Liters)
            .HasColumnName("liters")
            .HasColumnType("numeric(10,2)")
            .IsRequired()
            .HasComment("The litres the statement attributes to this line.");

        builder.Property(e => e.RedeemedAtUtc)
            .HasColumnName("redeemed_at_utc")
            .HasComment("When the supplier says the fuel was taken, if the statement carries a time.");

        builder.Property(e => e.VoucherId)
            .HasColumnName("voucher_id")
            .HasComment("The voucher this line matched. NULL when the number is unknown.");

        builder.HasOne(e => e.Voucher)
            .WithMany()
            .HasForeignKey(e => e.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.VoucherRedemptionId)
            .HasColumnName("voucher_redemption_id")
            .HasComment("The claim this line matched. NULL when no active claim existed.");

        builder.HasOne(e => e.VoucherRedemption)
            .WithMany()
            .HasForeignKey(e => e.VoucherRedemptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasComment("Where this line stands: matched, confirmed, mismatch, unknown and the rest.");

        builder.Property(e => e.Note)
            .HasColumnName("note")
            .HasMaxLength(500)
            .HasComment("The line as the supplier sent it, kept for the operator.");

        builder.Property(e => e.ImportedAtUtc)
            .HasColumnName("imported_at_utc")
            .IsRequired()
            .HasComment("When this line was imported.");

        builder.Property(e => e.ImportedByUserId)
            .HasColumnName("imported_by_user_id")
            .IsRequired()
            .HasComment("The operator who imported the statement. Bare uuid, no foreign key.");

        builder.Property(e => e.ResolvedAtUtc)
            .HasColumnName("resolved_at_utc")
            .HasComment("When the line was closed.");

        builder.Property(e => e.ResolvedByUserId)
            .HasColumnName("resolved_by_user_id")
            .HasComment("The operator who closed the line. Bare uuid, no foreign key.");

        builder.Property(e => e.ResolutionNote)
            .HasColumnName("resolution_note")
            .HasMaxLength(500)
            .HasComment("Why the operator closed the line.");

        builder.HasIndex(e => e.ExternalId)
            .IsUnique()
            .HasDatabaseName("ux_redemption_statement_rows_external_id");

        builder.HasIndex(e => new { e.Status, e.ImportedAtUtc });
        builder.HasIndex(e => e.VoucherNumber);
        builder.HasIndex(e => e.VoucherRedemptionId);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_redemption_statement_rows_status", """
                status IN ('Matched', 'Confirmed', 'Rejected', 'Reversed', 'UnknownVoucher',
                           'Mismatch', 'AlreadyConfirmed', 'NotAssigned', 'Dismissed')
                """);
            t.HasCheckConstraint("ck_redemption_statement_rows_liters_positive", "liters > 0");
            t.HasCheckConstraint(
                "ck_redemption_statement_rows_matched_has_voucher",
                "status <> 'Matched' OR voucher_id IS NOT NULL");
            t.HasCheckConstraint(
                "ck_redemption_statement_rows_resolved_shape",
                "status NOT IN ('Confirmed', 'Rejected', 'Reversed', 'Dismissed') "
                + "OR (resolved_at_utc IS NOT NULL AND resolved_by_user_id IS NOT NULL)");
        });
    }
}
