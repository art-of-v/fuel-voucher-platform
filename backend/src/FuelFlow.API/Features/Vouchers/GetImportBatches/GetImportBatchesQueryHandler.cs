using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetImportBatches;

public sealed class GetImportBatchesQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetImportBatchesQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ImportBatchDto>> HandleAsync(
        GetImportBatchesQuery query,
        CancellationToken cancellationToken = default)
    {
        return await _context.VoucherImports
            .AsNoTracking()
            .OrderByDescending(i => i.StartedAtUtc)
            .Select(i => new ImportBatchDto
            {
                Id = i.Id,
                FileName = i.FileName,
                PageCount = i.PageCount,
                StartedAtUtc = i.StartedAtUtc,
                CompletedAtUtc = i.CompletedAtUtc,
                Status = i.Status,
                ImportedCount = i.ImportedCount,
                DuplicateCount = i.DuplicateCount,
                FailedCount = i.FailedCount,
                VerificationFailedCount = i.VerificationFailedCount,
                VerifiedWithWarningsCount = i.VerifiedWithWarningsCount,
                VoucherCount = _context.FuelVouchers.Count(v => v.ImportJobId == i.Id)
            })
            .ToListAsync(cancellationToken);
    }
}
