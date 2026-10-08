using System.Text.Json;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Exchange;

/// <summary>The admin-entered cost/liter for one fuel in the new provider batch.</summary>
public sealed record ExchangeFuelCost(string FuelTypeId, decimal CostPerLiter);

/// <summary>
/// Records one operator→provider exchange (planning #104): retire the selected OLD stock vouchers,
/// activate the freshly-imported NEW ones at a cost that bakes in the доплата (so the remaining stock
/// reprices upward), pair old→new flexibly, and write an audit-grade <c>voucher_exchanges</c> row per
/// old voucher. No payment, no device signature — <c>Staff</c> policy only.
/// </summary>
public sealed record ConfirmVoucherExchangeCommand(
    List<Guid> OldVoucherIds,
    Guid NewImportId,
    List<ExchangeFuelCost> Costs,
    decimal SurchargeUah,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    Guid ActingUserId,
    string? ActingUserName);

public sealed record ExchangeFuelBlended(string FuelTypeId, decimal? BlendedCostPerLiter);

public sealed record ConfirmVoucherExchangeResult(
    bool Success,
    string? Error,
    Guid? ExchangeBatchId,
    int ExpiredCount,
    int NewActivatedCount,
    int PairedCount,
    int UnpairedOldCount,
    int UnpairedNewCount,
    decimal? BlendedCostPerLiter,
    IReadOnlyList<ExchangeFuelBlended> BlendedByFuel);

public sealed class ConfirmVoucherExchangeCommandHandler
{
    // Old stock we still allow retiring: anything that is unsold and not already deactivated/blocked.
    private static readonly VoucherStatus[] EligibleOldStatuses =
    {
        VoucherStatus.Available,
        VoucherStatus.Imported,
        VoucherStatus.VerifiedWithWarnings,
        VoucherStatus.Expired
    };

    private readonly ApplicationDbContext _context;
    private readonly BlendedCostRecalculator _recalculator;
    private readonly BulkActionVouchersCommandHandler _bulkAction;
    private readonly ProviderEventService _eventService;

    public ConfirmVoucherExchangeCommandHandler(
        ApplicationDbContext context,
        BlendedCostRecalculator recalculator,
        BulkActionVouchersCommandHandler bulkAction,
        ProviderEventService eventService)
    {
        _context = context;
        _recalculator = recalculator;
        _bulkAction = bulkAction;
        _eventService = eventService;
    }

    public async Task<ConfirmVoucherExchangeResult> HandleAsync(ConfirmVoucherExchangeCommand command, CancellationToken ct = default)
    {
        if (command.OldVoucherIds is null || command.OldVoucherIds.Count == 0)
            return Fail("Select at least one stock voucher to exchange.");
        if (command.SurchargeUah < 0m)
            return Fail("The surcharge (доплата) cannot be negative.");

        var oldIds = command.OldVoucherIds.Distinct().ToList();

        // Load the OLD vouchers tracked — we flip them to Expired (NoTracking would silently no-op).
        var olds = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .AsTracking()
            .Where(v => oldIds.Contains(v.Id))
            .ToListAsync(ct);

        if (olds.Count != oldIds.Count)
            return Fail("Some selected vouchers no longer exist.");

        foreach (var v in olds)
        {
            if (v.IsDeleted || v.AssignedToUserId != null || v.WorkerUserId != null)
                return Fail($"Voucher {v.VoucherNumber} is not operator stock (it is assigned, worker-held, or deleted).");
            if (!EligibleOldStatuses.Contains(v.Status))
                return Fail($"Voucher {v.VoucherNumber} has status {v.Status} and cannot be exchanged.");
            if (v.SupplierId is null)
                return Fail($"Voucher {v.VoucherNumber} has no supplier recorded. Set the supplier before exchanging it.");
            if (v.CostPerLiter is null)
                return Fail($"Voucher {v.VoucherNumber} has no cost recorded, so its value cannot be carried over to a replacement. Enter its cost first.");
        }

        // One exchange = one supplier. The vouchers go back to whoever issued them, so a mixed selection
        // would be settled with the wrong counterparty for part of the batch. Named explicitly rather than
        // silently split, because the operator's fix is to change the selection.
        var supplierIds = olds.Select(v => v.SupplierId!.Value).Distinct().ToList();
        if (supplierIds.Count > 1)
        {
            var names = await _context.Suppliers
                .AsNoTracking()
                .Where(s => supplierIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            var label = string.Join(", ", supplierIds.Select(id => names.TryGetValue(id, out var n) ? n : id.ToString()));
            return Fail($"An exchange must involve one supplier, but the selection spans: {label}.");
        }
        var supplierId = supplierIds[0];

        // Idempotency: refuse if any selected voucher was already recorded as exchanged.
        var alreadyExchanged = await _context.VoucherExchanges
            .AsNoTracking()
            .Where(x => oldIds.Contains(x.OldVoucherId))
            .CountAsync(ct);
        if (alreadyExchanged > 0)
            return Fail($"{alreadyExchanged} selected voucher(s) were already exchanged.");

        // Load the NEW vouchers as a NoTracking projection only — BulkAction loads+tracks them itself
        // during activation, so tracking them here would double-track the same keys and throw.
        var news = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .AsTracking()
            .Where(v => v.ImportJobId == command.NewImportId
                && !v.IsDeleted
                && (v.Status == VoucherStatus.Imported || v.Status == VoucherStatus.VerifiedWithWarnings))
            .Select(v => new VoucherExchangePairing.Candidate(v.Id, v.Provider, v.FuelTypeId, v.Liters, v.VoucherNumber))
            .ToListAsync(ct);

        if (news.Count == 0)
            return Fail("The uploaded import has no new vouchers to activate (unknown import, or all already processed).");

        // The invoice cost of the new paper, per fuel. Used for the surplus vouchers the operator did not
        // hand over — those are a fresh purchase, not a replacement, so they are valued at what the invoice
        // says. Paired vouchers are valued by carrying over their own old voucher instead.
        var invoiceCostByFuel = (command.Costs ?? new List<ExchangeFuelCost>())
            .Where(c => !string.IsNullOrWhiteSpace(c.FuelTypeId))
            .GroupBy(c => c.FuelTypeId)
            .ToDictionary(g => g.Key, g => g.Last().CostPerLiter);
        var newFuels = news.Select(n => n.FuelTypeId).Distinct().ToList();

        // planning #136 — the доплата is a real cost of the renewal, not just an audit note, so it lands in
        // the vouchers' own cost rather than only in the audit note. Spread the lump sum evenly across every
        // new liter, matching what was actually paid. The backend owns this fold so it can never be skipped
        // (it used to depend on the operator clicking an "apply surcharge" button in the UI).
        var totalNewLiters = news.Sum(n => n.Liters);
        var surchargePerLiter = totalNewLiters > 0m ? command.SurchargeUah / totalNewLiters : 0m;

        var newIds = news.Select(n => n.Id).ToList();
        var now = DateTime.UtcNow;

// Pair first: the pairing is what decides which old voucher each new one replaces, and therefore
        // whose cost carries over to it. Bucketing is by (provider, fuel, liters), so within a pair the two
        // have the same volume and the value transfer is exact.
        var pairing = VoucherExchangePairing.Pair(
            olds.Select(o => new VoucherExchangePairing.Candidate(o.Id, o.Provider, o.FuelTypeId, o.Liters, o.VoucherNumber)).ToList(),
            news);

        var oldById = olds.ToDictionary(o => o.Id);
        var newLitersById = news.ToDictionary(n => n.Id, n => n.Liters);
        var pairedNewIds = pairing.Pairs
            .Where(p => p.NewId.HasValue)
            .Select(p => p.NewId!.Value)
            .ToHashSet();

        // Stamp the new vouchers' own cost with ExecuteUpdate rather than tracked entities: production runs
        // NoTracking, and BulkAction re-loads these same rows and calls UpdateRange, so tracking them here
        // would attach the same key twice. Grouped by the computed value so a 500-voucher PDF is a handful
        // of statements instead of one per voucher.
        // Surplus vouchers the operator kept are an ordinary purchase, not a replacement, so they are valued at
        // the invoice price rather than inheriting anything. They still need a price: the activation gate
        // refuses to put uncosted stock on sale, and it applies to the whole activation call — so leaving
        // them priceless would fail the exchange itself, which is not what the operator means by "keep them".
        var surplusIds = newIds.Where(id => !pairedNewIds.Contains(id)).ToList();
        if (surplusIds.Count > 0)
        {
            var missing = surplusIds
                .Select(id => news.First(n => n.Id == id).FuelTypeId)
                .Distinct()
                .Where(f => !invoiceCostByFuel.TryGetValue(f, out var c) || c <= 0m)
                .ToList();
            if (missing.Count > 0)
            {
                return Fail(
                    "Enter the invoice cost per liter for every fuel in the uploaded PDF — including the " +
                    "vouchers you keep rather than exchange, or they cannot be activated: " +
                    string.Join(", ", missing));
            }
        }

        var newCostById = new Dictionary<Guid, decimal>();
        foreach (var (oldId, newId) in pairing.Pairs.Where(p => p.NewId.HasValue))
        {
            newCostById[newId!.Value] = decimal.Round(
                VoucherCosting.AfterExchange(oldById[oldId].CostPerLiter!.Value, command.SurchargeUah, totalNewLiters),
                4, MidpointRounding.AwayFromZero);
        }

        foreach (var id in surplusIds)
        {
            newCostById[id] = decimal.Round(
                invoiceCostByFuel[news.First(n => n.Id == id).FuelTypeId], 4, MidpointRounding.AwayFromZero);
        }

        foreach (var group in newCostById.GroupBy(kv => kv.Value))
        {
            var ids = group.Select(kv => kv.Key).ToList();
            await _context.FuelVouchers
                .IgnoreQueryFilters()
                .Where(v => ids.Contains(v.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(v => v.CostPerLiter, group.Key)
                    .SetProperty(v => v.SupplierId, supplierId)
                    .SetProperty(v => v.UpdatedAtUtc, now), ct);
        }

        // InMemory doesn't support transactions; the reuse handlers share this scoped context, so one
        // outer transaction makes their internal SaveChanges flush (not commit) and we commit once.
        await using var tx = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(ct)
            : null;

        // Expire the olds BEFORE repricing. The blended pool only counts Imported/VerifiedWithWarnings/
        // Available, so with the olds already Expired (and flushed by the save below) the reprice lands on
        // the post-exchange pool — no transient under-price.
        foreach (var v in olds)
        {
            v.Status = VoucherStatus.Expired;
            v.UpdatedAtUtc = now;
        }

        await _context.SaveChangesAsync(ct);

        var blendedByFuel = new List<ExchangeFuelBlended>();
        foreach (var fuel in newFuels)
        {
            var blended = await _recalculator.ComputeBlendedAsync(fuel, ct);
            var repriced = 0;
            if (blended is { } b)
            {
                repriced = await _recalculator.RepriceAsync(fuel, b, command.ActingUserId, ct);
                await _context.SaveChangesAsync(ct);
            }

            blendedByFuel.Add(new ExchangeFuelBlended(fuel, blended));
        }

        // Cost gate is now satisfied → activate the new vouchers (also kicks the fulfillment worker; benign).
        var activateResult = await _bulkAction.HandleAsync(
            new BulkActionVouchersCommand("activate", newIds, null, command.ActingUserId, command.ActingUserName),
            ct);
        if (!activateResult.Success)
            return Fail(activateResult.Error ?? "Failed to activate the new vouchers.");

        // One row per OLD voucher, each carrying ITS OWN surcharge share and cost. The old vouchers of one
        // exchange routinely cost different amounts per liter, so a batch-wide copy would misreport every
        // row but one during supplier reconciliation.
        var batchId = Guid.NewGuid();
        foreach (var (oldId, newId) in pairing.Pairs)
        {
            var old = oldById[oldId];
            var pairLiters = newId.HasValue ? newLitersById[newId.Value] : old.Liters;
            var pairSurcharge = totalNewLiters > 0m
                ? decimal.Round(command.SurchargeUah * pairLiters / totalNewLiters, 2, MidpointRounding.AwayFromZero)
                : 0m;

            _context.VoucherExchanges.Add(new VoucherExchange
            {
                Id = Guid.NewGuid(),
                ExchangeBatchId = batchId,
                OldVoucherId = oldId,
                NewVoucherId = newId,
                FuelTypeId = old.FuelTypeId,
                Provider = old.Provider,
                SurchargeUah = pairSurcharge,
                CostPerLiterApplied = newId.HasValue
                    ? newCostById.GetValueOrDefault(newId.Value)
                    : null,
                SupplierId = supplierId,
                InvoiceNumber = command.InvoiceNumber,
                InvoiceDate = command.InvoiceDate,
                ActingUserId = command.ActingUserId,
                ActingUserName = command.ActingUserName,
                CreatedAtUtc = now
            });
        }

        await _context.SaveChangesAsync(ct);

        await _eventService.RecordEventAsync(
            "Voucher",
            batchId.ToString(),
            "VoucherExchanged",
            null,
            JsonSerializer.Serialize(new
            {
                exchangeBatchId = batchId,
                oldCount = olds.Count,
                newCount = news.Count,
                paired = pairing.PairedCount,
                unpairedOld = pairing.UnpairedOldCount,
                unpairedNew = pairing.UnpairedNewCount,
                surchargeUah = command.SurchargeUah,
                invoiceNumber = command.InvoiceNumber
            }),
            command.ActingUserId,
            command.ActingUserName,
            $"Exchanged {olds.Count} stock voucher(s) → {news.Count} new; доплата {command.SurchargeUah:0.##} UAH",
            "all",
            ct);

        if (tx is not null)
            await tx.CommitAsync(ct);

        var singleBlended = blendedByFuel.Count == 1 ? blendedByFuel[0].BlendedCostPerLiter : null;
        return new ConfirmVoucherExchangeResult(
            Success: true,
            Error: null,
            ExchangeBatchId: batchId,
            ExpiredCount: olds.Count,
            NewActivatedCount: activateResult.Count,
            PairedCount: pairing.PairedCount,
            UnpairedOldCount: pairing.UnpairedOldCount,
            UnpairedNewCount: pairing.UnpairedNewCount,
            BlendedCostPerLiter: singleBlended,
            BlendedByFuel: blendedByFuel);
    }

    private static ConfirmVoucherExchangeResult Fail(string error)
        => new(false, error, null, 0, 0, 0, 0, 0, null, Array.Empty<ExchangeFuelBlended>());
}
