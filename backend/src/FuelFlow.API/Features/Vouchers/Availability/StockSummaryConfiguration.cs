using FuelFlow.Features.Vouchers.Availability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Availability;

/// <summary>
/// The table the trigger keeps, mapped as the read it is.
/// </summary>
/// <remarks>
/// <para>
/// Keyless would have been the honest shape - the trigger writes it and nothing else does - but
/// EF cannot track a keyless entity, and a test that seeds stock has to be able to write one. So
/// the row has an id, and the id says nothing: the group is what makes a row unique, and the
/// database holds that with a unique index over the seven columns below.
/// </para>
/// <para>
/// The column types are the voucher's own, so the two tables cannot drift: a group is a group of
/// vouchers, and the values in it are the values on the rows.
/// </para>
/// </remarks>
internal sealed class StockSummaryConfiguration : IEntityTypeConfiguration<StockSummary>
{
    public void Configure(EntityTypeBuilder<StockSummary> builder)
    {
        builder.ToTable("stock_summary");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd()
            .HasComment("The row's own key: a uuid, like every other key in the schema. It says nothing - the group below is what makes the row unique.");

        builder.Property(e => e.Brand)
            .HasColumnName("brand")
            .HasMaxLength(50)
            .HasComment("The network as the voucher rows spell it - the same text the operator's list filters on.");

        builder.Property(e => e.FuelTypeId)
            .HasColumnName("fuel_type_id")
            .HasComment("The grade.");

        builder.Property(e => e.Liters)
            .HasColumnName("liters")
            .HasColumnType("numeric(10,2)")
            .HasComment("The size, in litres.");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasComment("The vouchers' status. The storefront sells only 'Available'; the operator's list shows every status.");

        builder.Property(e => e.DataKind)
            .HasColumnName("data_kind")
            .HasMaxLength(10)
            .HasComment("Which pool the vouchers belong to - 'real' or 'qa'.");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasComment("Whether the rows are soft-deleted: the operator's list counts them, the storefront does not sell them.");

        builder.Property(e => e.SupplierExpirationDate)
            .HasColumnName("supplier_expiration_date")
            .HasComment("The supplier's expiry date - the storefront does not sell what has expired.");

        builder.Property(e => e.VoucherCount)
            .HasColumnName("voucher_count")
            .HasComment("How many vouchers are in this group.");
    }
}
