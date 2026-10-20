using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Exchange;

/// <summary>
/// The recorded exchange batches, newest first, optionally narrowed to one supplier or a date range.
/// </summary>
/// <param name="Page">1-based page number.</param>
/// <param name="Limit">Rows per page; clamped server-side by <see cref="PageLimits"/>.</param>
/// <param name="SupplierId">Only batches settled with this supplier.</param>
/// <param name="From">Inclusive lower bound on the exchange date.</param>
/// <param name="To">Inclusive upper bound on the exchange date.</param>
public sealed record GetVoucherExchangeBatchesQuery(
    int Page = 1,
    int Limit = 20,
    Guid? SupplierId = null,
    DateOnly? From = null,
    DateOnly? To = null);

/// <summary>
/// Reads the exchange audit as batches — the unit an operator actually reconciles.
///
/// Every exchange writes one <see cref="VoucherExchange"/> row per retired voucher, so the table's
/// natural grain is a single pair. What an operator asks is about the batch: who it was with, how many
/// vouchers moved, what it cost in total. Folding the pairs here means the totals come from the same
/// rows the drill-down shows.
///
/// Before this existed the only way to answer any of it was SQL, and the one screen that did answer it
/// — the per-batch cost timeline — got the total wrong, reading a single row's share instead of the
/// bucket's sum.
/// </summary>
public sealed class GetVoucherExchangeBatchesQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetVoucherExchangeBatchesQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<VoucherExchangeBatchesResponse> HandleAsync(
        GetVoucherExchangeBatchesQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = PageLimits.ClampPage(query.Page);
        var limit = PageLimits.ClampPageSize(query.Limit);

        // The audit rows, narrowed by the filters. A retired voucher may since have been
        // soft-deleted, and an audit row about it must stay readable — so the voucher lookups below
        // ignore the model's global filter and ask for the row by id, as the cost timeline does.
        var rows = _context.VoucherExchanges
            .AsNoTracking()
            .AsQueryable();

        if (query.SupplierId != null && query.SupplierId != Guid.Empty)
            rows = rows.Where(e => e.SupplierId == query.SupplierId.Value);

if (query.From != null)
            rows = rows.Where(e => e.CreatedAtUtc >= StartOfDayUtc(query.From.Value));

        if (query.To != null)
        {
            // Inclusive of the whole end day: the operator types a date, not an instant.
            var endExclusive = StartOfDayUtc(query.To.Value).AddDays(1);
            rows = rows.Where(e => e.CreatedAtUtc < endExclusive);
        }

        // A confirm can span several fuels, so the batch is keyed by id alone and the fuels it
        // touched are reported as a list rather than picked.
        //
        // The voucher lookups are spelled out inline rather than pulled into helper methods: these
        // sit inside aggregate functions, where EF cannot translate a call into SQL. Each one is the
        // same shape — find the voucher by id, ignoring the soft-delete filter, because an audit row
        // must outlive the stock it is about.
        var grouped = rows
            .GroupBy(e => e.ExchangeBatchId)
            .Select(g => new
            {
                BatchId = g.Key,
                AtUtc = g.Min(e => e.CreatedAtUtc),
                SupplierId = g.First().SupplierId,
                Providers = g.Select(e => e.Brand).Distinct().OrderBy(p => p).ToList(),
                FuelTypeIds = g.Select(e => e.FuelTypeId).Distinct().OrderBy(f => f).ToList(),
                VouchersExchanged = g.Count(),
                LitersOut = g.Sum(e => _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == e.OldVoucherId)
                    .Select(v => v.Liters)
                    .FirstOrDefault()),
                // Each row carries only its OWN share of the surcharge, so the batch's charge is the
                // sum of them. Reading one row reports a fraction of it.
                SurchargeUah = g.Sum(e => e.SurchargeUah),
                // Retired vouchers of one exchange routinely cost different amounts per liter, so
                // there is no single "before" to report. Sum the litres-weighted cost and divide
                // once, below: a 50 L voucher must count for five 10 L ones.
                WeightedCostSum = g.Sum(e =>
                    _context.FuelVouchers
                        .IgnoreQueryFilters()
                        .Where(v => v.Id == e.OldVoucherId)
                        .Select(v => v.Liters)
                        .FirstOrDefault()
                    * (_context.FuelVouchers
                        .IgnoreQueryFilters()
                        .Where(v => v.Id == e.OldVoucherId)
                        .Select(v => v.CostPerLiter)
                        .FirstOrDefault() ?? 0m)),
                PricedPairs = g.Count(e => _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Any(v => v.Id == e.OldVoucherId && v.CostPerLiter != null)),
                CostPerLiterAfter = g.Select(e => e.CostPerLiterApplied).FirstOrDefault(c => c != null),
                RecordedBy = g.Select(e => e.ActingUserName).FirstOrDefault(n => n != null)
            })
            .OrderByDescending(e => e.AtUtc)
            .ThenBy(e => e.BatchId);

        var total = await grouped.CountAsync(cancellationToken);

        var items = await grouped
            .Skip(PageLimits.SkipFor(page, limit))
            .Take(limit)
            .ToListAsync(cancellationToken);

var suppliers = await _context.Suppliers
            .AsNoTracking()
            .Where(s => _context.VoucherExchanges.Any(e => e.SupplierId == s.Id))
            .OrderBy(s => s.Name)
            .Select(s => new VoucherExchangeSupplierOption { Id = s.Id, Name = s.Name })
            .ToListAsync(cancellationToken);

        var supplierNames = suppliers.ToDictionary(s => s.Id, s => s.Name);

        return new VoucherExchangeBatchesResponse
        {
            Data = items.Select(e => new VoucherExchangeBatchDto
            {
                ExchangeBatchId = e.BatchId,
                AtUtc = e.AtUtc,
                SupplierId = e.SupplierId,
                SupplierName = supplierNames.GetValueOrDefault(e.SupplierId),
                Providers = e.Providers,
                FuelTypeIds = e.FuelTypeIds,
                VouchersExchanged = e.VouchersExchanged,
                LitersOut = e.LitersOut,
                SurchargeUah = e.SurchargeUah,
                // Null unless every retired voucher's cost is known: a partial mean of the pairs we
                // do know would read as a firmer number than the record supports.
                CostPerLiterBefore = e.PricedPairs == e.VouchersExchanged && e.LitersOut > 0m
                    ? decimal.Round(e.WeightedCostSum / e.LitersOut, 4)
                    : null,
                CostPerLiterAfter = e.CostPerLiterAfter,
                RecordedBy = e.RecordedBy
            }).ToList(),
            Total = total,
            Page = page,
            Limit = limit,
Suppliers = suppliers
        };
    }

    // The column is timestamptz. A DateOnly widened with ToDateTime comes out Kind=Unspecified,
    // which Npgsql refuses to compare against it — and it refuses at query time, so an unfixed date
    // filter is a 500 on the screen that owns the date picker.
private static DateTime StartOfDayUtc(DateOnly date)
        => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
}

public sealed class VoucherExchangeBatchesResponse
{
    public List<VoucherExchangeBatchDto> Data { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int Limit { get; set; }

    /// <summary>Suppliers that appear in the audit, for the filter dropdown.</summary>
    public List<VoucherExchangeSupplierOption> Suppliers { get; set; } = [];
}

public sealed class VoucherExchangeSupplierOption
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class VoucherExchangeBatchDto
{
    public Guid ExchangeBatchId { get; set; }
    public DateTime AtUtc { get; set; }
    public Guid SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public List<string> Providers { get; set; } = [];
    public List<Guid> FuelTypeIds { get; set; } = [];
    public int VouchersExchanged { get; set; }
    public decimal LitersOut { get; set; }

    /// <summary>What the brand charged for the whole exchange: the sum of every pair's share.</summary>
    public decimal SurchargeUah { get; set; }

    /// <summary>
    /// Litres-weighted mean of what the retired vouchers cost. Null unless every one of them is known —
    /// the vouchers in one exchange deliberately cost different amounts per liter.
    /// </summary>
    public decimal? CostPerLiterBefore { get; set; }

    /// <summary>Cost per liter applied to the replacements.</summary>
    public decimal? CostPerLiterAfter { get; set; }

    public string? RecordedBy { get; set; }
}