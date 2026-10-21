using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Orders.Configurations;

internal sealed class OrderLineItemConfiguration : IEntityTypeConfiguration<OrderLineItem>
{
    public void Configure(EntityTypeBuilder<OrderLineItem> builder)
    {
        builder.ToTable("order_line_items");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.OrderId)
            .HasColumnName("order_id")
            .IsRequired().HasComment("The order this line belongs to.");

        builder.Property(e => e.Brand)
            .HasColumnName("brand")
            .HasMaxLength(50)
            .IsRequired()
            .HasComment(                "The brand's NAME - what a report groups by and what a parser reads. The id below is what" +
                    "the database can check, and the two are the same fact written twice.");

        // The key the brand text above is a copy of. Required, because the composite foreign key
        // below is what refuses a row whose brand is not its fuel's brand - and a foreign key with a
        // NULL in it is not compared at all, so a nullable key would be a key the database does not
        // check.
        builder.Property(e => e.BrandId)
            .HasColumnName("brand_id")
            .HasColumnType("uuid")
            .IsRequired()
            .HasComment(                "The brand's id - the key the text above is a copy of. The composite foreign key" +
                    "(fuel_type_id, brand_id) -> fuel_types(id, brand_id) is what refuses a row whose brand" +
                    "is not its fuel's brand. A nullable key would be a key the database does not check," +
                    "because a foreign key with a NULL in it is not compared at all.");

        // The fuel id is a uuid since planning D-21: a uuid column, so this can carry a real
        // foreign key one day - which is what D-02 is waiting for.
           builder.Property(e => e.FuelTypeId)
               .HasColumnName("fuel_type_id")
               .HasColumnType("uuid")
               .IsRequired().HasComment("The fuel this line is for.");

        builder.Property(e => e.Liters)
            .HasColumnName("liters")
            .HasColumnType("numeric(10,2)")
            .IsRequired()
            .HasComment(                "How many litres one unit of this line is.");

        builder.Property(e => e.Quantity)
            .HasColumnName("quantity")
            .HasDefaultValue(1)
            .IsRequired()
            .HasComment(                "How many units of this line the customer bought.");

        builder.Property(e => e.UnitPrice)
            .HasColumnName("unit_price")
            .HasColumnType("numeric(12,2)")
            .IsRequired()
            .HasComment(                "What the customer paid for ONE unit, in UAH.");

        builder.Property(e => e.LineTotal)
            .HasColumnName("line_total")
            .HasColumnType("numeric(12,2)")
            .IsRequired()
            .HasComment(                "What the customer paid for the WHOLE line, in UAH. This is the truth of the money;" +
                    "original_line_total is the reference it is measured against.");

        builder.Property(e => e.OriginalLineTotal)
            .HasColumnName("original_line_total")
            .HasColumnType("numeric(12,2)")
            .HasComment(                "The pump-based LIST total for this line, in UAH, frozen at checkout. The customer's" +
                    "saving against the pump is original_line_total - line_total. NULL for rows predating the" +
                    "column (no backfill - a historical pump price cannot be reconstructed) and for lines" +
                    "with no pump reference (a voucher renewal), which therefore contribute no saving.");

        builder.Property(e => e.TermCode)
            .HasColumnName("term_code")
            .HasMaxLength(8)
            .HasComment(                "The term the customer bought this fuel for, frozen at checkout. NULL means the full" +
                    "remaining supplier term was sold. A renewal line ALWAYS has one.");

        // Null unless the line is a renewal. RESTRICT because the voucher a customer paid to
        // renew is evidence, and it must not be deletable out from under the line that names it.
        builder.Property(e => e.SourceVoucherId)
            .HasColumnName("source_voucher_id")
            .HasComment(                "The voucher this line renews, when the line is a renewal; NULL on every other kind of" +
                    "line. It lives on the LINE and not on the fulfilment: a renewal is an intent - it exists" +
                    "from checkout, before payment and before any stock is assigned - and a paid renewal that" +
                    "finds no stock has no fulfilment row at all.");

        builder.HasOne(e => e.SourceVoucher)
            .WithMany()
            .HasForeignKey(e => e.SourceVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.RenewalBranch)
            .HasColumnName("renewal_branch")
            .HasMaxLength(16)
            .HasComment(                "Which fulfilment path this renewal took: 'extend' or 'replace'. NULL on every other kind" +
                    "of line. Stored rather than derived, because the branch is DECIDED AND PRICED at" +
                    "checkout from the voucher's state that day, while fulfilment deliberately re-decides it" +
                    "from the current state - the voucher can lapse in between - so nothing reading it back" +
                    "later can recompute the original answer.");

        builder.Property(e => e.PreviousCustomerExpiration)
            .HasColumnName("previous_customer_expiration")
            .HasComment(                "The customer's expiry on the source voucher BEFORE this renewal was applied. NULL until" +
                    "fulfilment, and on every non-renewal line. A snapshot, because there is nothing left to" +
                    "read it from afterwards: the extend branch overwrites" +
                    "fuel_vouchers.customer_expiration_date in place, and no history column survives it.");

        builder.Property(e => e.NewCustomerExpiration)
            .HasColumnName("new_customer_expiration")
            .HasComment(                "The expiry this renewal gave the customer. On a REPLACE this is the PROMISE, not the" +
                    "replacement voucher's real life: what the customer bought is what they can rely on, and" +
                    "any longer real life on the stock voucher is a hidden reserve they may extend again" +
                    "later - and showing it here would be selling twice.");

        builder.Property(e => e.PricingBasis)
            .HasColumnName("pricing_basis")
            .HasColumnType("jsonb")
            .HasComment(                "The inputs that produced unit_price, frozen at checkout, as a JSON object: the per-litre" +
                    "cost, the margin, the pump price, the minimum discount and the terms id this line was" +
                    "priced under. unit_price records WHAT the customer paid; this records WHY. NULL for" +
                    "every order placed before the column existed, and deliberately not backfilled:" +
                    "backfilling from today's contract would manufacture a history that never happened, which" +
                    "is worse than an honest gap - it looks like evidence.");

        builder.HasOne(e => e.Order)
            .WithMany(o => o.LineItems)
            .HasForeignKey(e => e.OrderId)
            // RESTRICT, not Cascade, and this is the last table in the schema where a money line
            // cascaded away with its order. It took over from voucher_renewal_items.order_id when that
            // table was dissolved: the renewal item was the only thing stopping an order a customer paid
            // a renewal on from being deleted, and order_line_items.order_id cascaded in exactly the
            // wrong direction. A line without its order is not a line, and neither is a renewal the
            // customer paid for.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.OrderId);
        builder.HasIndex(e => e.FuelTypeId);
        builder.HasIndex(e => e.Brand);

        // Fulfilment loads a renewal order's lines by their source voucher, and the customer timeline
        // walks the replacement chain through it. Without an index both are a scan of every line ever
        // placed.
        builder.HasIndex(e => e.SourceVoucherId)
            .HasDatabaseName("ix_order_line_items_source_voucher_id");

        builder.ToTable(t =>
        {
            // line_total is unit_price * quantity, computed in the handler. Nothing enforced that
            // the inputs were even sane - a zero-litre line or a negative price was accepted.
            t.HasCheckConstraint("ck_order_line_positive_quantity", "quantity > 0");
            t.HasCheckConstraint("ck_order_line_positive_liters", "liters > 0");
            t.HasCheckConstraint("ck_order_line_non_negative_prices",
                "unit_price >= 0 AND line_total >= 0");
            t.HasCheckConstraint("ck_order_line_original_non_negative",
                "original_line_total IS NULL OR original_line_total >= 0");

            // The same two words operator_voucher_renewals uses, so the three places that answer
            // "extend or replace?" share one vocabulary.
            t.HasCheckConstraint("ck_order_line_renewal_branch",
                "renewal_branch IS NULL OR renewal_branch IN ('extend','replace')");

            // A line either is a renewal, carrying a source voucher and a branch, or it is not.
            // Half a renewal is the state that reads as "renewal" in one query and "buy-fuel" in
            // another, which is how the branch ends up derived from two different answers.
            t.HasCheckConstraint("ck_order_line_renewal_pair_is_whole",
                "(renewal_branch IS NULL) = (source_voucher_id IS NULL)");

            // The term is the INPUT to the branch decision (VoucherRenewalEligibility.CanExtend) and
            // to the expiry the customer is given. A renewal line without one cannot be fulfilled.
            //
            // This is the check whose absence cost a day: renewal lines were created with a null
            // term_code and the term lived only on voucher_renewal_items, so the line described the
            // purchase without stating what was bought for it. The term now lives here and nowhere
            // else.
            t.HasCheckConstraint("ck_order_line_renewal_has_term",
                "renewal_branch IS NULL OR term_code IS NOT NULL");

            // A renewal moves time forward, or it would not be one. Zero-length or backwards is a
            // mis-parse, not a renewal, and the customer's history renders this pair as a date range.
            t.HasCheckConstraint("ck_order_line_renewal_expirations_ordered",
                "previous_customer_expiration IS NULL OR new_customer_expiration IS NULL " +
                "OR new_customer_expiration > previous_customer_expiration");

            // Both snapshots are written together, in one transaction, after fulfilment decides the
            // branch. Half of them is the state where the customer's history shows a start with no
            // end - or worse, a range that reads as if nothing happened yet.
            t.HasCheckConstraint("ck_order_line_renewal_snapshots_are_paired",
                "(previous_customer_expiration IS NULL) = (new_customer_expiration IS NULL)");
        });

        // --- F-14: the foreign key this column was missing -------------------------------------------------
        // The reference is real and single-parent, so the database enforces it. ON DELETE is stated
        // explicitly rather than left to the default: a schema that says NO ACTION by accident is a
        // schema nobody has thought about.
        //
        // The key is the PAIR (fuel type, brand), not the fuel alone - which is what D-21's second
        // slice needs. A foreign key over one column says "this fuel exists"; a foreign key over two
        // says "this fuel exists AND this line's brand is that fuel's brand", and the second half is
        // the fact the brand text on this row is a copy of. The pair points at the fuel's own
        // alternate key (id, brand_id), so the key spans one hop instead of two.
        //
        // Both halves of the pair are NOT NULL, which is what makes the key a key: a foreign key
        // with a NULL in it is not compared at all, so a nullable brand_id would be a brand the
        // database never checks.
        builder.HasOne<FuelTypeEntity>()
            .WithMany()
            .HasForeignKey(e => new { e.FuelTypeId, e.BrandId })
            .HasPrincipalKey(e => new { e.Id, e.BrandId })
            .OnDelete(DeleteBehavior.Restrict);

        // Postgres does not index a foreign key column on its own, and the parent's delete check and
        // every join down this column scan without one.
        builder.HasIndex(e => e.FuelTypeId);
    }
}
