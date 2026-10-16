using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Renewal;

internal sealed class VoucherRenewalItemConfiguration : IEntityTypeConfiguration<VoucherRenewalItem>
{
    public void Configure(EntityTypeBuilder<VoucherRenewalItem> builder)
    {
        builder.ToTable("voucher_renewal_items");

        builder.HasKey(e => e.Id);

        // #246 detector: xmin token. No DDL - xmin is a system column. Raw SQL must never write
        // this table, or the token starts failing saves that did not actually race.
        builder.UseXminAsConcurrencyToken();

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(e => e.SourceVoucherId)
            .HasColumnName("source_voucher_id")
            .IsRequired();

        builder.Property(e => e.LineItemId)
            .HasColumnName("line_item_id");

        builder.Property(e => e.TermCode)
            .HasColumnName("term_code")
            .HasMaxLength(8)
            .IsRequired();

        // Bare done-marker column, not a navigation: it may point at the source voucher (extend
        // branch) or a fresh stock voucher (replace branch). Constrained — the SourceVoucher FK already
        // pins the row to fuel_vouchers, and this stays a nullable audit field.
        builder.Property(e => e.FulfilledVoucherId)
            .HasColumnName("fulfilled_voucher_id");

        builder.Property(e => e.FulfilledAtUtc)
            .HasColumnName("fulfilled_at_utc");

        builder.Property(e => e.AmountPaid)
            .HasColumnName("amount_paid")
            .HasColumnType("numeric(12,2)");

        builder.Property(e => e.PreviousCustomerExpiration)
            .HasColumnName("previous_customer_expiration");

        builder.Property(e => e.NewCustomerExpiration)
            .HasColumnName("new_customer_expiration");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasOne(e => e.Order)
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            // Restrict, for the same reason as refunds->orders: this row is what the customer paid
            // and how the term moved. Deleting the order must not erase the payment record.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SourceVoucher)
            .WithMany()
            .HasForeignKey(e => e.SourceVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        // The delivered side carries the same RESTRICT as the source side (#217) - see
        // VoucherExchangeConfiguration for why both are constrained together.
        builder.HasOne(e => e.FulfilledVoucher)
            .WithMany()
            .HasForeignKey(e => e.FulfilledVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

builder.HasIndex(e => e.OrderId);
builder.HasIndex(e => e.SourceVoucherId);

        // Read by the fulfilment path when it links the renewal's voucher to the line it charged, and
        // by the per-line reconciliation that now groups renewals the same way as purchases.
        builder.HasIndex(e => e.LineItemId)
            .HasDatabaseName("ix_voucher_renewal_items_line_item_id");

        // Restrict, same reason as every other money-ish reference here: the pairing of a charge to
        // the line that carried it is a fact about the transaction, not a convenience.
        builder.HasOne<OrderLineItem>()
            .WithMany()
            .HasForeignKey(e => e.LineItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // FulfilledVoucherId gets its index from the FK convention below - declaring one here as
        // well would emit a second index under the same generated name.
        builder.ToTable(t =>
        {
            // Mirrors order_line_items.line_total, which is numeric(12,2). numeric(18,2) was
            // three orders of magnitude wider than the money it is compared against.
            t.HasCheckConstraint("ck_voucher_renewal_amount_non_negative",
                "amount_paid IS NULL OR amount_paid >= 0");
        });
    }
}

