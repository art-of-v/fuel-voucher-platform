using FuelFlow.Features.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Import;

public interface IImportVouchersDbContext
{
    DbSet<FuelVoucher> FuelVouchers { get; }
    DbSet<VoucherImport> VoucherImports { get; }
    DbSet<VoucherImportError> VoucherImportErrors { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
