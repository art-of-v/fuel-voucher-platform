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
    public string FuelTypeId { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;

    /// <summary>Vouchers of this import that this exchange retired.</summary>
    public int VouchersExchangedOut { get; init; }

    /// <summary>Liters that left the batch.</summary>
    public decimal LitersExchangedOut { get; init; }

    /// <summary>
    /// What the provider charged for the exchange. Taken from ONE row rather than summed:
    /// <c>voucher_exchanges</c> replicates the batch total onto every row of the exchange for audit, so
    /// a Sum here would multiply the money by the number of vouchers.
    /// </summary>
    public decimal SurchargeUah { get; init; }

    /// <summary>Cost/liter the surrendered vouchers carried before the exchange.</summary>
    public decimal? CostPerLiterBefore { get; init; }

    /// <summary>Cost/liter applied to the replacements.</summary>
    public decimal? CostPerLiterAfter { get; init; }

    public string? InvoiceNumber { get; init; }
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
/// Built as an aggregation over <c>voucher_exchanges</c> rather than a new ledger table, because
/// that table already holds the raw record of every exchange - which vouchers went out, the surcharge,
/// the resulting cost/liter, the invoice - and it is a durable entity, not a transport queue. (The
/// <c>BatchCostEntered</c> provider event also records cost entries, but provider events are an
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
        var exchangeRows = await _context.VoucherExchanges
            .AsNoTracking()
            .Where(e => _context.FuelVouchers
                .IgnoreQueryFilters()
                .Any(v => v.Id == e.OldVoucherId && v.ImportJobId == query.ImportId))
            .Select(e => new
            {
                e.ExchangeBatchId,
                e.OldVoucherId,
                e.FuelTypeId,
                e.Provider,
                e.SurchargeUah,
                e.CostPerLiterApplied,
                e.InvoiceNumber,
                e.CreatedAtUtc,
                OldLiters = _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == e.OldVoucherId)
                    .Select(v => (decimal?)v.Liters)
                    .FirstOrDefault(),
                OldCostPerLiter = _context.FuelVouchers
                    .IgnoreQueryFilters()
                    .Where(v => v.Id == e.OldVoucherId)
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
                ExchangeBatchId = g.Key.ExchangeBatchId,
                AtUtc = g.Min(e => e.CreatedAtUtc),
                FuelTypeId = g.Key.FuelTypeId,
                Provider = g.First().Provider,
                VouchersExchangedOut = g.Count(),
                LitersExchangedOut = g.Sum(e => e.OldLiters ?? 0m),
                // SurchargeUah is replicated onto every row of the exchange; the rows of one bucket
                // therefore share it, so Distinct().First() is the batch total - never a Sum.
                SurchargeUah = g.Select(e => e.SurchargeUah).Distinct().First(),
                CostPerLiterBefore = g.Max(e => e.OldCostPerLiter),
                CostPerLiterAfter = g.First().CostPerLiterApplied,
                InvoiceNumber = g.Select(e => e.InvoiceNumber).FirstOrDefault(n => n != null)
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