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
        var importBatches = await _context.VoucherImports
            .AsNoTracking()
            .OrderByDescending(i => i.StartedAtUtc)
            .ToListAsync(cancellationToken);

        var importIds = importBatches.Select(i => i.Id).ToList();

        var voucherCounts = await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.ImportJobId != null && importIds.Contains(v.ImportJobId.Value))
            .GroupBy(v => v.ImportJobId!.Value)
            .Select(g => new { ImportJobId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ImportJobId, g => g.Count, cancellationToken);

        return importBatches.Select(i => new ImportBatchDto
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
            VoucherCount = voucherCounts.GetValueOrDefault(i.Id)
        }).ToList();
    }
}
