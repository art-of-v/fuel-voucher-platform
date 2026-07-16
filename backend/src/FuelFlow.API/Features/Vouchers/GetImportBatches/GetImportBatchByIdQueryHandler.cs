using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetImportBatches;

public sealed class GetImportBatchByIdQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetImportBatchByIdQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ImportBatchDto?> HandleAsync(
        GetImportBatchByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await _context.VoucherImports
            .AsNoTracking()
            .Where(i => i.Id == query.Id)
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
            .FirstOrDefaultAsync(cancellationToken);
    }
}
