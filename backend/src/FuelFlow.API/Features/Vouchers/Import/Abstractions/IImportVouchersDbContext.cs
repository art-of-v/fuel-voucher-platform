using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Import;

public interface IImportVouchersDbContext
{
    DbSet<FuelVoucher> FuelVouchers { get; }
    DbSet<QrParameters> QrParameters { get; }
    DbSet<VoucherImport> VoucherImports { get; }
    DbSet<VoucherImportError> VoucherImportErrors { get; }
    DbSet<Supplier> Suppliers { get; }

    /// <summary>
    /// The ledger the import writes a <c>deposit</c> into, so a voucher's life has a first entry.
    /// </summary>
    /// <remarks>
    /// This interface exists to keep the import's database surface narrow on purpose - it is the
    /// bulkiest write path in the system and should not be able to reach anything it does not use. A
    /// deposit is something it uses, so it is named here rather than the handler being handed the whole
    /// context.
    /// </remarks>
    DbSet<VoucherMovement> VoucherMovements { get; }

    /// <summary>
    /// Read to check that the brand printed on a voucher sells the fuel it claims: the brand is this
    /// row's <c>brand_id</c>, and <c>brand</c> on the voucher is the same fact written as free text.
    /// </summary>
    DbSet<FuelTypeEntity> FuelTypes { get; }

    /// <summary>
    /// Read for the same check, and only for it: <c>fuel_types.brand_id</c> is a uuid since planning
    /// D-21 while the paper says a name, so the id has to be resolved to a slug before the two can be
    /// compared. Added rather than resolved by <c>BrandSlugResolver</c>, which would have meant handing
    /// this interface the whole <c>ApplicationDbContext</c> — the narrowing this file exists to protect.
    /// </summary>
    DbSet<Brand> Brands { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

