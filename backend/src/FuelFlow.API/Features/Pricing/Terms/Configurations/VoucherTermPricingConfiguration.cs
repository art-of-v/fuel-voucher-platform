using FuelFlow.Features.Pricing.Terms.SharedModels;
using FuelFlow.Features.Vouchers.Renewal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Pricing.Terms.Configurations;

internal sealed class VoucherTermPricingConfiguration : IEntityTypeConfiguration<VoucherTermPricing>
{
    public void Configure(EntityTypeBuilder<VoucherTermPricing> builder)
    {
        builder.ToTable("voucher_term_pricing");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Scheme)
            .HasColumnName("scheme")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.TermCode)
            .HasColumnName("term_code")
            .HasMaxLength(4)
            .IsRequired();

        builder.Property(e => e.Enabled)
            .HasColumnName("enabled")
            .IsRequired();

        // 14,4 like pricing_rules.value and fuel_offers.cost_per_liter, so the three per-litre rate
        // columns are comparable in a query rather than needing a cast.
        builder.Property(e => e.RatePerLiter)
            .HasColumnName("rate_per_liter")
            .HasColumnType("numeric(14,4)")
            .IsRequired();

        builder.Property(e => e.ValidFrom)
            .HasColumnName("valid_from")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.ValidTo)
            .HasColumnName("valid_to")
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");

        builder.Property(e => e.UpdatedByUserName)
            .HasColumnName("updated_by_user_name")
            .HasMaxLength(200);

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(e => e.UpdatedByUserId)
            .HasDatabaseName("ix_voucher_term_pricing_updated_by")
            .HasFilter("updated_by_user_id IS NOT NULL");

        builder.ToTable(t =>
        {
            // The scheme set VoucherTermPricingSchemes documents.
            t.HasCheckConstraint("ck_voucher_term_pricing_scheme",
                $"scheme IN ('{string.Join("','", VoucherTermPricingSchemes.All)}')");

            // The tier vocabulary VoucherRenewalTerms.Code emits. Pinned here as well because a psql
            // session writing a tier does not go through the handler - and a typo would otherwise
            // produce a row the application silently never reads.
            t.HasCheckConstraint("ck_voucher_term_pricing_term_code",
                "term_code IN ('" + string.Join("','", VoucherRenewalTerms.All.Select(t2 => t2.Code())) + "')");

            // A negative price is not a discount, and a negative renewal price pays the customer.
            // Stated here rather than only in C# for the same reason the scheme check is.
            t.HasCheckConstraint("ck_voucher_term_pricing_rate_non_negative", "rate_per_liter >= 0");

            // An end before a start is not a closed period, it is a typo.
            t.HasCheckConstraint("ck_voucher_term_pricing_valid_window",
                "valid_to IS NULL OR valid_to > valid_from");
        });

        // The invariant the read path depends on: for a (scheme, term) there is exactly one current
        // price, and a closed row may never be reopened. Partial on valid_to IS NULL, so this is
        // "one OPEN row per tier", not "one row per tier" - history is the point of the table.
        builder.HasIndex(e => new { e.Scheme, e.TermCode })
            .IsUnique()
            .HasDatabaseName("ux_voucher_term_pricing_open_tier")
            .HasFilter("valid_to IS NULL");

        // The read: every open row for one scheme. Both loaders run on the quote and checkout paths,
        // so the index is sized for them rather than for a full history scan.
        builder.HasIndex(e => new { e.Scheme, e.ValidFrom, e.ValidTo })
            .HasDatabaseName("ix_voucher_term_pricing_current");
    }
}