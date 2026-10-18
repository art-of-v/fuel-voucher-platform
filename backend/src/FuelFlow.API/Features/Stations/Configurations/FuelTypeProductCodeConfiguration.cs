using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Brands.Configurations;

/// <summary>
/// <c>fuel_type_product_codes</c> — the QR product codes a network's vouchers carry, and the grade
/// each one means at that network. Planning #349, step 3b.
/// </summary>
internal sealed class FuelTypeProductCodeConfiguration : IEntityTypeConfiguration<FuelTypeProductCode>
{
    public void Configure(EntityTypeBuilder<FuelTypeProductCode> builder)
    {
        builder.ToTable("fuel_type_product_codes");

        // (fuel, code) rather than (network, code): the pair is what the paper says, and the network
        // is derived from the fuel. The constraint that matters is the other index below.
        builder.HasKey(e => new { e.FuelTypeId, e.ProductCode });

        builder.Property(e => e.FuelTypeId).HasColumnName("fuel_type_id").HasColumnType("text").IsRequired();
        builder.Property(e => e.BrandId).HasColumnName("brand_id").HasColumnType("text").IsRequired();
        builder.Property(e => e.ProductCode).HasColumnName("product_code").HasColumnType("text").IsRequired();
        builder.Property(e => e.LastSeenAtUtc).HasColumnName("last_seen_at_utc").HasColumnType("timestamptz");

        // THE guarantee. One code, one grade, per network: the lookup a voucher's QR does is a single
        // equality here, and two grades claiming one code would send every voucher of that paper to
        // whichever row the database happened to return first — which is how a real voucher ends up
        // priced against the wrong fuel.
        //
        // It leads with brand_id because the code is scoped to the network that publishes it, and
        // because that is the lookup: a parser knows its brand and asks for its own codes.
        builder.HasIndex(e => new { e.BrandId, e.ProductCode })
            .IsUnique()
            .HasDatabaseName("ux_fuel_type_product_codes_brand_code");

        builder.HasIndex(e => e.ProductCode)
            .HasDatabaseName("ix_fuel_type_product_codes_code");

        builder.ToTable(t =>
        {
            // 100 is the bound every catalog text column shares (F-24); the codes themselves are four
            // or five digits, and the fuel type id they point at is bounded at 100 by its own CHECK.
            t.HasCheckConstraint("ck_fuel_type_product_codes_fuel_type_id_length", "length(fuel_type_id) <= 100");
            t.HasCheckConstraint("ck_fuel_type_product_codes_brand_id_length", "length(brand_id) <= 100");

            // Bounded at 32 rather than 100: this is a printed number, not prose. A 33-character
            // "product code" is a paste of the wrong field.
            t.HasCheckConstraint("ck_fuel_type_product_codes_product_code_length", "length(product_code) <= 32");

            // A code that is blank or whitespace is not a code. The primary key would happily accept
            // '   ', and a lookup that matches '' can then claim every voucher with no code at all.
            t.HasCheckConstraint("ck_fuel_type_product_codes_code_not_blank", "btrim(product_code) <> ''");
        });
    }
}
