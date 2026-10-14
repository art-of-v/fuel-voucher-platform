using FuelFlow.Features.Orders.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

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
            .IsRequired();

        builder.Property(e => e.Brand)
            .HasColumnName("brand")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.FuelTypeId)
            .HasColumnName("fuel_type_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Liters)
            .HasColumnName("liters")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(e => e.Quantity)
            .HasColumnName("quantity")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(e => e.UnitPrice)
            .HasColumnName("unit_price")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(e => e.LineTotal)
            .HasColumnName("line_total")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(e => e.OriginalLineTotal)
            .HasColumnName("original_line_total")
            .HasColumnType("numeric(12,2)");

        builder.Property(e => e.TermCode)
            .HasColumnName("term_code")
            .HasMaxLength(8);

        builder.Property(e => e.PricingBasis)
            .HasColumnName("pricing_basis")
            .HasColumnType("jsonb");

        builder.HasOne(e => e.Order)
            .WithMany(o => o.LineItems)
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.OrderId);
        builder.HasIndex(e => e.FuelTypeId);
        builder.HasIndex(e => e.Brand);

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
        });
    }
}
