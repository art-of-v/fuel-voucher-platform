using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Exchange;

/// <summary>One exchange batch: who it was with, what it covered, and every pair inside it.</summary>
/// <param name="ExchangeBatchId">The batch's id.</param>
public sealed record GetVoucherExchangeBatchQuery(Guid ExchangeBatchId);

/// <summary>
/// One batch, pair by pair.
///
/// The list gives the batch total; this gives the evidence for it. Each row here is one retired
/// voucher and the replacement that took its place, carrying the cost that voucher actually had and
/// the share of the surcharge that pair alone accounts for.
///
/// Both vouchers are fetched by id rather than through navigations, even though
/// <c>new_voucher_id</c> is now a real foreign key (4dd6128a). A navigation would apply the model's
/// global <c>!IsDeleted</c> filter, and an audit row has to outlive the stock it is about: the
/// replacements are routinely soft-deleted once they are sold or retired, and an exchange that
/// vanishes from the history because its stock was cleaned up is precisely the quiet history loss
/// the audit exists to prevent. The by-id lookups keep <c>IgnoreQueryFilters</c> in reach for that.
/// </summary>
public sealed class GetVoucherExchangeBatchQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetVoucherExchangeBatchQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<VoucherExchangeBatchDetailDto?> HandleAsync(
        GetVoucherExchangeBatchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.ExchangeBatchId == Guid.Empty)
            return null;

        var rows = await _context.VoucherExchanges
            .AsNoTracking()
            .Where(e => e.ExchangeBatchId == query.ExchangeBatchId)
            .Select(e => new
            {
                e.Id,
                e.OldVoucherId,
                e.NewVoucherId,
                e.FuelTypeId,
                e.Brand,
                e.SupplierId,
                e.ActingUserName,
                e.SurchargeUah,
                e.CostPerLiterApplied,
                e.CreatedAtUtc,
                OldNumber = _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == e.OldVoucherId)
                    .Select(v => v.VoucherNumber)
                    .FirstOrDefault(),
                OldLiters = _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == e.OldVoucherId)
                    .Select(v => (decimal?)v.Liters)
                    .FirstOrDefault(),
                OldCostPerLiter = _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == e.OldVoucherId)
                    .Select(v => (decimal?)v.CostPerLiter)
                    .FirstOrDefault(),
                NewNumber = e.NewVoucherId == null
                    ? null
                    : _context.FuelVouchers
                        .IgnoreQueryFilters()
                        .Where(v => v.Id == e.NewVoucherId.Value)
                        .Select(v => v.VoucherNumber)
                        .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return null;

        // A confirm is one event: it stamps one timestamp and one operator across its rows, so the
        // batch's identity comes from the first row rather than four more round trips.
        var head = rows[0];

        return new VoucherExchangeBatchDetailDto
        {
            ExchangeBatchId = query.ExchangeBatchId,
            AtUtc = rows.Min(r => r.CreatedAtUtc),
            SupplierId = head.SupplierId,
            SupplierName = await _context.Suppliers
                .AsNoTracking()
                .Where(s => s.Id == head.SupplierId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(cancellationToken),
            RecordedBy = rows.FirstOrDefault(r => r.ActingUserName is not null)?.ActingUserName,
            TotalSurchargeUah = rows.Sum(r => r.SurchargeUah),
            TotalLiters = rows.Sum(r => r.OldLiters ?? 0m),
            Pairs = rows
                .OrderBy(r => r.OldNumber)
                .Select(r => new VoucherExchangePairDto
                {
                    OldVoucherId = r.OldVoucherId,
                    OldVoucherNumber = r.OldNumber,
                    OldLiters = r.OldLiters,
                    OldCostPerLiter = r.OldCostPerLiter,
                    NewVoucherId = r.NewVoucherId,
                    NewVoucherNumber = r.NewNumber,
                    FuelTypeId = r.FuelTypeId,
                    Brand = r.Brand,
                    /// <summary>This pair's own share of the batch surcharge — not the batch total.</summary>
                    SurchargeShareUah = r.SurchargeUah,
                    NewCostPerLiter = r.CostPerLiterApplied
                })
                .ToList()
        };
    }
}

public sealed class VoucherExchangeBatchDetailDto
{
    public Guid ExchangeBatchId { get; set; }
    public DateTime AtUtc { get; set; }
    public Guid SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? RecordedBy { get; set; }

    /// <summary>Every pair's share added up — the figure the list shows for this batch.</summary>
    public decimal TotalSurchargeUah { get; set; }

    public decimal TotalLiters { get; set; }
    public List<VoucherExchangePairDto> Pairs { get; set; } = [];
}

public sealed class VoucherExchangePairDto
{
    public Guid OldVoucherId { get; set; }
    public string? OldVoucherNumber { get; set; }
    public decimal? OldLiters { get; set; }

    /// <summary>What this voucher cost us — its own, not the batch's average.</summary>
    public decimal? OldCostPerLiter { get; set; }

    public Guid? NewVoucherId { get; set; }
    public string? NewVoucherNumber { get; set; }
    public string FuelTypeId { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;

    /// <summary>This pair's own share of the surcharge. One row of the batch total, never the total.</summary>
    public decimal SurchargeShareUah { get; set; }

    /// <summary>Cost per liter carried over to the replacement.</summary>
    public decimal? NewCostPerLiter { get; set; }
}