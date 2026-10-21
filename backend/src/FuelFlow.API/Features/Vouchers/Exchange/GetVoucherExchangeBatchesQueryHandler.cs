using FuelFlow.Features.Vouchers.Movements;
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
/// Every exchange writes one <see cref="VoucherMovement"/> row per retired voucher, so the ledger's
/// natural grain for an exchange is a single pair. What an operator asks is about the batch: who it
/// was with, how many vouchers moved, what it cost in total. Folding the pairs here means the
/// totals come from the same rows the drill-down shows.
///
/// Before this existed the only way to answer any of it was SQL, and the one screen that did answer it
/// — the per-batch cost timeline — got the total wrong, reading a single row's share instead of
/// the bucket's sum.
///
/// The ledger is where the pairs live now (planning #319): the row of the retired paper carries the
/// counterpart, that pair's share of the doplata and the cost stamped on the replacement, and the
/// settlement's own facts — the supplier, the total, the operator, the time — live on the batch row.
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
        //
        // The voucher is JOINED, not fetched by a subquery: the brand and the fuel are the retired
        // voucher's own (they cannot change), and a group-by cannot carry a collection subquery.
        // A hard-deleted voucher would drop the pair — it cannot happen: the ledger's foreign key on
        // the paper is RESTRICT.
        var rows = from m in _context.VoucherMovements.AsNoTracking()
                   where m.Kind == VoucherMovementKinds.ExchangeOut
                   join v in _context.FuelVouchers.IgnoreQueryFilters() on m.VoucherId equals v.Id
                   select new
                   {
                       m.BatchId,
                       m.VoucherId,
                       m.OccurredAtUtc,
                       m.SurchargeShareUah,
                       m.AppliedCostPerLiter,
                       v.Brand,
                       v.FuelTypeId,
                       v.Liters,
                       v.CostPerLiter
                   };

        // The supplier is the settlement's fact, not the pair's: the batch says who it was with, and
        // every pair of that batch is with the same one.
        if (query.SupplierId != null && query.SupplierId != Guid.Empty)
        {
            var supplierId = query.SupplierId.Value;
            rows = rows.Where(m => _context.MovementBatches.Any(b => b.Id == m.BatchId && b.SupplierId == supplierId));
        }

        if (query.From != null)
            rows = rows.Where(m => m.OccurredAtUtc >= StartOfDayUtc(query.From.Value));

        if (query.To != null)
        {
            // Inclusive of the whole end day: the operator types a date, not an instant.
            var endExclusive = StartOfDayUtc(query.To.Value).AddDays(1);
            rows = rows.Where(m => m.OccurredAtUtc < endExclusive);
        }

        // A confirm can span several fuels, so the batch is keyed by id alone and the fuels it
        // touched are reported as a list rather than picked.
        var grouped = rows
            .GroupBy(m => m.BatchId)
            .Select(g => new
            {
                BatchId = g.Key,
                AtUtc = g.Min(m => m.OccurredAtUtc),
                SupplierId = _context.MovementBatches
                    .Where(b => b.Id == g.Key)
                    .Select(b => b.SupplierId)
                    .FirstOrDefault(),
                Providers = g.Select(m => m.Brand).Distinct().OrderBy(p => p).ToList(),
                FuelTypeIds = g.Select(m => m.FuelTypeId).Distinct().OrderBy(f => f).ToList(),
                VouchersExchanged = g.Count(),
                LitersOut = g.Sum(m => m.Liters),
                // Each pair carries only its OWN share of the surcharge, so the batch's charge is the
                // sum of them. Reading one pair reports a fraction of it.
                SurchargeUah = g.Sum(m => m.SurchargeShareUah ?? 0m),
                // Retired vouchers of one exchange routinely cost different amounts per liter, so
                // there is no single "before" to report. Sum the litres-weighted cost and divide
                // once, below: a 50 L voucher must count for five 10 L ones.
                WeightedCostSum = g.Sum(m => m.Liters * (m.CostPerLiter ?? 0m)),
                PricedPairs = g.Count(m => m.CostPerLiter != null),
                CostPerLiterAfter = g.Select(m => m.AppliedCostPerLiter).FirstOrDefault(c => c != null),
                RecordedBy = _context.MovementBatches
                    .Where(b => b.Id == g.Key)
                    .Select(b => b.OpenedByUserName)
                    .FirstOrDefault()
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
            .Where(s => _context.MovementBatches.Any(b =>
                b.SupplierId == s.Id
                && b.Kind == MovementBatchKinds.VoucherExchange
                && _context.VoucherMovements.Any(m => m.BatchId == b.Id && m.Kind == VoucherMovementKinds.ExchangeOut)))
            .OrderBy(s => s.Name)
            .Select(s => new VoucherExchangeSupplierOption { Id = s.Id, Name = s.Name })
            .ToListAsync(cancellationToken);

        var supplierNames = suppliers.ToDictionary(s => s.Id, s => s.Name);

        return new VoucherExchangeBatchesResponse
        {
            Data = items.Select(e => new VoucherExchangeBatchDto
            {
                ExchangeBatchId = e.BatchId ?? Guid.Empty,
                AtUtc = e.AtUtc,
                // The settlement's supplier. A batch whose header is missing is a data defect, and
                // reads as "no supplier" rather than as a crash.
                SupplierId = e.SupplierId ?? Guid.Empty,
                SupplierName = supplierNames.GetValueOrDefault(e.SupplierId ?? Guid.Empty),
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