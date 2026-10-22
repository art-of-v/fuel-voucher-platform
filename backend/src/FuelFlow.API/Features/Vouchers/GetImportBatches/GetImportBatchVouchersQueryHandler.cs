using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetImportBatches;

/// <summary>
/// The vouchers of one import batch, a page at a time, with the count of the whole batch.
/// </summary>
/// <remarks>
/// <para>
/// The list used to be the whole batch: every voucher of the import, materialised, on every
/// open of the tab. A supplier PDF is a thousand vouchers, and the tab answered with a
/// thousand rows to draw a table that shows a screenful.
/// </para>
/// <para>
/// The count travels with the page, because the admin has to know what the page is a part of -
/// and because the two buttons that act on the whole batch need the batch's size to say what
/// they are about to do.
/// </para>
/// </remarks>
public sealed class GetImportBatchVouchersQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetImportBatchVouchersQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ImportBatchVouchersPage> HandleAsync(
        GetImportBatchVouchersQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = PageLimits.ClampPage(query.Page);
        var pageSize = PageLimits.ClampPageSize(query.PageSize);

        var batch = _context.FuelVouchers
            .IncludingSoftDeleted()
            .Where(v => v.ImportJobId == query.ImportId);

        var totalCount = await batch.CountAsync(cancellationToken);

        var items = await batch
            .AsNoTracking()
            .Include(v => v.FuelType)
            .OrderBy(v => v.CreatedAtUtc)
            // The tie-break: vouchers imported from one PDF share their creation time, and
            // without a unique second key the sort is unstable - a row can move between pages.
            .ThenBy(v => v.Id)
            .Skip(PageLimits.SkipFor(page, pageSize))
            .Take(pageSize)
            .Select(v => new ImportBatchVoucherDto
            {
                Id = v.Id,
                Brand = v.Brand,
                FuelTypeId = v.FuelTypeId,
                FuelTypeName = v.FuelType != null ? v.FuelType.Name : null,
                Liters = v.Liters,
                SupplierExpirationDate = v.SupplierExpirationDate,
                CustomerExpirationDate = v.CustomerExpirationDate,
                VoucherNumber = v.VoucherNumber,
                Status = v.Status.ToString(),
                CreatedAtUtc = v.CreatedAtUtc,
                VerificationMismatchPercent = v.VerificationMismatchPercent,
                VerificationMismatchedModules = v.VerificationMismatchedModules,
                VerificationTotalModules = v.VerificationTotalModules
            })
            .ToListAsync(cancellationToken);

        return new ImportBatchVouchersPage(items, totalCount, page, pageSize);
    }
}

/// <summary>A page of an import batch's vouchers, and the size of the batch it is a page of.</summary>
public sealed record ImportBatchVouchersPage(
    List<ImportBatchVoucherDto> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
