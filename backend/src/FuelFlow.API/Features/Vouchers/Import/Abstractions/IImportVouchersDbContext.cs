using FuelFlow.Features.Vouchers;
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
    /// Read to check that the brand printed on a voucher sells the fuel it claims: the brand is this
    /// row's <c>station_id</c>, and <c>provider</c> on the voucher is the same fact written as free text.
    /// </summary>
    DbSet<FuelTypeEntity> FuelTypes { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

