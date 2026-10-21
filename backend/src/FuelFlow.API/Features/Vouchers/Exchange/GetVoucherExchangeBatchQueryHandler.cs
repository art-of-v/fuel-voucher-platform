using FuelFlow.Features.Vouchers.Movements;
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
/// Both vouchers are fetched by id rather than through navigations. A navigation would apply the
/// model's global <c>!IsDeleted</c> filter, and an audit row has to outlive the stock it is about: the
/// replacements are routinely soft-deleted once they are sold or retired, and an exchange that
/// vanishes from the history because its stock was cleaned up is precisely the quiet history loss
/// the audit exists to prevent. The by-id lookups keep <c>IgnoreQueryFilters</c> in reach for that.
///
/// The pairs are the ledger's rows (planning #319): the row of the retired paper names the
/// replacement, the pair's share of the doplata and the cost stamped on the replacement.
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

        var rows = await _context.VoucherMovements
            .AsNoTracking()
            .Where(m => m.Kind == VoucherMovementKinds.ExchangeOut && m.BatchId == query.ExchangeBatchId)
            .Select(m => new
            {
                m.Id,
                OldVoucherId = m.VoucherId,
                NewVoucherId = m.ReplacedByVoucherId,
                FuelTypeId = _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == m.VoucherId)
                    .Select(v => v.FuelTypeId)
                    .FirstOrDefault(),
                Brand = _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == m.VoucherId)
                    .Select(v => v.Brand)
                    .FirstOrDefault(),
                SurchargeUah = m.SurchargeShareUah,
                CostPerLiterApplied = m.AppliedCostPerLiter,
                CreatedAtUtc = m.OccurredAtUtc,
                OldNumber = _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == m.VoucherId)
                    .Select(v => v.VoucherNumber)
                    .FirstOrDefault(),
                OldLiters = _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == m.VoucherId)
                    .Select(v => (decimal?)v.Liters)
                    .FirstOrDefault(),
                OldCostPerLiter = _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == m.VoucherId)
                    .Select(v => (decimal?)v.CostPerLiter)
                    .FirstOrDefault(),
                NewNumber = m.ReplacedByVoucherId == null
                    ? null
                    : _context.FuelVouchers
                        .IgnoreQueryFilters()
                        .Where(v => v.Id == m.ReplacedByVoucherId.Value)
                        .Select(v => v.VoucherNumber)
                        .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return null;

        // The header comes from the batch row, which is what it is FOR (#319). It used to be
        // reconstructed from the members: `rows.Min(r => r.CreatedAtUtc)` for the time, `rows[0]` for
        // the supplier, "the first row with a name" for the operator. That was an assumption - that
        // one confirm stamps one timestamp and one operator across its rows - and it happened to hold
        // because the handler minted the batch id in-process. Stating the header once removes the
        // assumption, and makes a batch whose members were written across two transactions say so.
        //
        // A missing header is a data defect rather than a reason to fail the screen, so the members
        // still answer; that can only happen on a database migrated without the backfill.
        var batch = await _context.MovementBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == query.ExchangeBatchId, cancellationToken);

        // Hoisted: a null-propagating operator inside an EF predicate is an expression-tree error,
        // and the value is needed twice anyway. The supplier is the settlement's fact now: the batch
        // says who it was with, and every pair of that batch is with the same one.
        var supplierId = batch?.SupplierId ?? Guid.Empty;

        return new VoucherExchangeBatchDetailDto
        {
            ExchangeBatchId = query.ExchangeBatchId,
            AtUtc = batch?.OpenedAtUtc ?? rows.Min(r => r.CreatedAtUtc),
            Status = batch?.Status,
            SupplierId = supplierId,
            SupplierName = await _context.Suppliers
                .AsNoTracking()
                .Where(s => s.Id == supplierId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(cancellationToken),
            RecordedBy = batch?.OpenedByUserName,
            // The batch total is the amount the brand charged, which the sum of shares only reproduces
            // when every retired voucher was paired. See MovementBatch.SurchargeUah.
            TotalSurchargeUah = batch?.SurchargeUah ?? rows.Sum(r => r.SurchargeUah ?? 0m),
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
                    SurchargeShareUah = r.SurchargeUah ?? 0m,
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

    /// <summary>
    /// The settlement's lifecycle state, from the batch row. Null only on a database migrated without
    /// the backfill; the screen keeps working, because the pair evidence below is unaffected.
    /// </summary>
    public string? Status { get; set; }

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
    public Guid FuelTypeId { get; set; }
    public string Brand { get; set; } = string.Empty;

    /// <summary>This pair's own share of the surcharge. One row of the batch total, never the total.</summary>
    public decimal SurchargeShareUah { get; set; }

    /// <summary>Cost per liter carried over to the replacement.</summary>
    public decimal? NewCostPerLiter { get; set; }
}