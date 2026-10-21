using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.PurchaseBatchCost;

/// <summary>How one batch's cost got to where it is: the exchanges that took stock out of it.</summary>
public sealed record GetImportBatchCostTimelineQuery(Guid ImportId);

/// <summary>
/// One exchange that consumed vouchers belonging to this import.
/// </summary>
public sealed record BatchCostTimelineEntry
{
    public Guid ExchangeBatchId { get; init; }
    public DateTime AtUtc { get; init; }
    public Guid FuelTypeId { get; init; }
    public string Brand { get; init; } = string.Empty;

    /// <summary>Vouchers of this import that this exchange retired.</summary>
    public int VouchersExchangedOut { get; init; }

    /// <summary>Liters that left the batch.</summary>
    public decimal LitersExchangedOut { get; init; }

    /// <summary>
    /// What the brand charged for the exchange: the bucket's whole surcharge, summed from the rows.
    /// Each row carries only its OWN share of the batch surcharge - the share of that pair's liters -
    /// so one row is a fraction of the total and the bucket's surcharge is the sum of them. Taking a
    /// single row instead would under-report it by the number of vouchers exchanged.
    /// </summary>
    public decimal SurchargeUah { get; init; }

    /// <summary>Cost/liter the surrendered vouchers carried before the exchange.</summary>
    public decimal? CostPerLiterBefore { get; init; }

    /// <summary>Cost/liter applied to the replacements.</summary>
    public decimal? CostPerLiterAfter { get; init; }
}

/// <summary>Where a batch stands now, alongside what happened to get it here.</summary>
public sealed record ImportBatchCostTimeline
{
    public Guid ImportId { get; init; }
    public IReadOnlyList<BatchCostTimelineEntry> Entries { get; init; } = [];
    public IReadOnlyList<ImportBatchCostDto> CurrentCosts { get; init; } = [];
}

/// <summary>
/// Reads the costing timeline of one import: every exchange that took vouchers out of this batch,
/// with what it cost, and where the batch's cost stands now.
/// </summary>
/// <remarks>
/// <para>
/// Built as an aggregation over the movement ledger rather than a new table of its own, because the
/// ledger already holds the raw record of every exchange - which vouchers went out, the surcharge,
/// the resulting cost/liter - and it is a durable ledger, not a transport queue. (The
/// <c>BatchCostEntered</c> brand event also records cost entries, but brand events are an
/// outbox: they exist to be dispatched and are not a ledger to audit against.)
/// </para>
/// <para>
/// Scope, settled with the owner: where the surcharge is *attributed* between the old and the new
/// batch does not move money, because stock is valued as a litres-weighted average over the whole
/// pool (<see cref="BlendedCostRecalculator"/>). So this timeline attributes each exchange to the
/// batch whose stock it consumed - the batch where the loss actually happened - and says so plainly,
/// rather than presenting itself as the place where the accounting decision is made.
/// </para>
/// </remarks>
public sealed class GetImportBatchCostTimelineQueryHandler
{
    private readonly ApplicationDbContext _context;
    private readonly GetImportBatchCostsQueryHandler _currentCostsHandler;

    public GetImportBatchCostTimelineQueryHandler(
        ApplicationDbContext context,
        GetImportBatchCostsQueryHandler currentCostsHandler)
    {
        _context = context;
        _currentCostsHandler = currentCostsHandler;
    }

    public async Task<ImportBatchCostTimeline> HandleAsync(
        GetImportBatchCostTimelineQuery query,
        CancellationToken cancellationToken = default)
    {
        // Only exchanges whose RETIRED voucher belonged to this import - that is what makes an
        // exchange part of this batch's costing story.
        //
        // The pairs are the ledger's rows (planning #319): the row of the retired paper names the
        // replacement, the pair's share of the doplata and the cost stamped on the replacement.
        var exchangeRows = await _context.VoucherMovements
            .AsNoTracking()
            .Where(m => m.Kind == VoucherMovementKinds.ExchangeOut
                && _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Any(v => v.Id == m.VoucherId && v.ImportJobId == query.ImportId))
            .Select(m => new
            {
                ExchangeBatchId = m.BatchId,
                OldVoucherId = m.VoucherId,
                FuelTypeId = _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == m.VoucherId)
                    .Select(v => (Guid?)v.FuelTypeId)
                    .FirstOrDefault() ?? Guid.Empty,
                Brand = _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == m.VoucherId)
                    .Select(v => v.Brand)
                    .FirstOrDefault() ?? string.Empty,
                SurchargeUah = m.SurchargeShareUah,
                CostPerLiterApplied = m.AppliedCostPerLiter,
                CreatedAtUtc = m.OccurredAtUtc,
                OldLiters = _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == m.VoucherId)
                    .Select(v => (decimal?)v.Liters)
                    .FirstOrDefault(),
                OldCostPerLiter = _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == m.VoucherId)
                    .Select(v => (decimal?)v.CostPerLiter)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        // One entry per confirm action. An exchange pairs vouchers 1:1 inside a fuel bucket, so the
        // rows of one ExchangeBatchId describe a single event and are folded together here.
        var entries = exchangeRows
            .GroupBy(e => new { e.ExchangeBatchId, e.FuelTypeId })
            .Select(g => new BatchCostTimelineEntry
            {
                ExchangeBatchId = g.Key.ExchangeBatchId ?? Guid.Empty,
                AtUtc = g.Min(e => e.CreatedAtUtc),
                FuelTypeId = g.Key.FuelTypeId,
                Brand = g.First().Brand,
                VouchersExchangedOut = g.Count(),
                LitersExchangedOut = g.Sum(e => e.OldLiters ?? 0m),
                // Summed, because each pair carries only its own share of the surcharge. The pairs of one
                // fuel bucket are one pair of same-volume vouchers each, so their shares add up to the
                // bucket's surcharge exactly.
                SurchargeUah = g.Sum(e => e.SurchargeUah ?? 0m),
                CostPerLiterBefore = g.Max(e => e.OldCostPerLiter),
                CostPerLiterAfter = g.First().CostPerLiterApplied
            })
            .OrderBy(e => e.AtUtc)
            .ToList();

        var current = await _currentCostsHandler.HandleAsync(
            new GetImportBatchCostsQuery(query.ImportId),
            cancellationToken);

        return new ImportBatchCostTimeline
        {
            ImportId = query.ImportId,
            Entries = entries,
            CurrentCosts = current
        };
    }
}