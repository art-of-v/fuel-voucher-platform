using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetImportBatches;

public sealed class GetImportBatchVouchersQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetImportBatchVouchersQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ImportBatchVoucherDto>> HandleAsync(
        GetImportBatchVouchersQuery query,
        CancellationToken cancellationToken = default)
    {
        return await _context.FuelVouchers
            .AsNoTracking()
            .Include(v => v.FuelType)
            .Where(v => v.ImportJobId == query.ImportId)
            .OrderBy(v => v.CreatedAtUtc)
            .Select(v => new ImportBatchVoucherDto
            {
                Id = v.Id,
                Provider = v.Provider,
                FuelTypeId = v.FuelTypeId,
                FuelTypeName = v.FuelType != null ? v.FuelType.Name : null,
                Liters = v.Liters,
                ExpirationDate = v.ExpirationDate,
                VoucherNumber = v.VoucherNumber,
                Status = v.Status.ToString(),
                CreatedAtUtc = v.CreatedAtUtc,
                VerificationMismatchPercent = v.VerificationMismatchPercent,
                VerificationMismatchedModules = v.VerificationMismatchedModules,
                VerificationTotalModules = v.VerificationTotalModules
            })
            .ToListAsync(cancellationToken);
    }
}
