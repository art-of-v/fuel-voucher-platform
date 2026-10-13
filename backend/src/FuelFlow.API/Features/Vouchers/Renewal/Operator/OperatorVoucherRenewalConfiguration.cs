using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Renewal.Operator;

internal sealed class OperatorVoucherRenewalConfiguration : IEntityTypeConfiguration<OperatorVoucherRenewal>
{
    public void Configure(EntityTypeBuilder<OperatorVoucherRenewal> builder)
    {
        builder.ToTable("operator_voucher_renewals");

        builder.HasKey(e => e.Id);

        // #246 detector: xmin token. No DDL - xmin is a system column. Raw SQL must never write
        // this table, or the token starts failing saves that did not actually race.
        builder.UseXminAsConcurrencyToken();

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.VoucherId)
            .HasColumnName("voucher_id")
            .IsRequired();

        // Nullable audit link to the stock voucher on the replace branch, null on extend. Constrained
        // below, like the source side (#217).
        builder.Property(e => e.ReplacementVoucherId)
            .HasColumnName("replacement_voucher_id");

        builder.Property(e => e.CustomerUserId)
            .HasColumnName("customer_user_id")
            .IsRequired();

        builder.Property(e => e.Branch)
            .HasColumnName("branch")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.TermCode)
            .HasColumnName("term_code")
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(e => e.OldExpiration)
            .HasColumnName("old_expiration")
            .IsRequired();

        builder.Property(e => e.NewExpiration)
            .HasColumnName("new_expiration")
            .IsRequired();

        builder.Property(e => e.SurchargeUah)
            .HasColumnName("surcharge_uah")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(e => e.InvoiceNumber)
            .HasColumnName("invoice_number")
            .HasMaxLength(100);

        builder.Property(e => e.InvoiceDate)
            .HasColumnName("invoice_date");

        builder.Property(e => e.ActingUserId)
            .HasColumnName("acting_user_id")
            .IsRequired();

        builder.Property(e => e.ActingUserName)
            .HasColumnName("acting_user_name")
            .HasMaxLength(200);

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasOne(e => e.Voucher)
            .WithMany()
            .HasForeignKey(e => e.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        // The replacement side carries the same RESTRICT as the source side (#217) - see
        // VoucherExchangeConfiguration for why both are constrained together. Nullable, because an
        // extend has no replacement.
        builder.HasOne(e => e.ReplacementVoucher)
            .WithMany()
            .HasForeignKey(e => e.ReplacementVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.VoucherId);
        builder.HasIndex(e => e.CustomerUserId);
        builder.HasIndex(e => e.CreatedAtUtc);
        // ReplacementVoucherId is indexed by the FK convention above.

        // customer_user_id and acting_user_id stay unconstrained on purpose. They are written
        // from the acting account's claim, and a renewal must never be refused because of an
        // id the audit row carries - the same reasoning as error_logs and provider_event_outbox,
        // and as feedback_messages.user_id, which makes the same argument for the same reason.
        // The cost is a dangling id; the benefit is that the settlement record can always be
        // written.
        //
        // The distinction from the two voucher columns above is the whole point: those point at
        // domain objects that must exist for the row to mean anything, so they are constrained
        // Restrict. These two point at who was acting. A claim can name an account that is since
        // gone, and a settlement record that refuses to be written over that is worse than a row
        // carrying an id nobody can resolve.
        //
        // This comment used to end by deferring to "the review's F-14". F-14 in the security audit
        // is "backup restore never exercised" and says nothing about referential integrity, so the
        // reference sent a reader to the wrong document to find a decision that is fully argued
        // here. The trade-off is stated in the four lines above instead.

        builder.ToTable(t =>
        {
            // Exactly these two values, per the entity's own documentation.
            t.HasCheckConstraint("ck_operator_renewal_branch", "branch IN ('extend', 'replace')");
            t.HasCheckConstraint("ck_operator_renewal_expiration_moves_forward",
                "new_expiration > old_expiration");
            // 0 legitimately means "free compensation", so >= 0 and not > 0.
            t.HasCheckConstraint("ck_operator_renewal_surcharge_non_negative", "surcharge_uah >= 0");
        });
    }
}
