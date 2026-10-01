using System.Text.Json;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
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
    private readonly SetBatchCostCommandHandler _setBatchCost;
    private readonly BulkActionVouchersCommandHandler _bulkAction;
    private readonly ProviderEventService _eventService;

    public ConfirmVoucherExchangeCommandHandler(
        ApplicationDbContext context,
        SetBatchCostCommandHandler setBatchCost,
        BulkActionVouchersCommandHandler bulkAction,
        ProviderEventService eventService)
    {
        _context = context;
        _setBatchCost = setBatchCost;
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
        }

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
            .AsNoTracking()
            .Where(v => v.ImportJobId == command.NewImportId
                && !v.IsDeleted
                && (v.Status == VoucherStatus.Imported || v.Status == VoucherStatus.VerifiedWithWarnings))
            .Select(v => new VoucherExchangePairing.Candidate(v.Id, v.Provider, v.FuelTypeId, v.Liters, v.VoucherNumber))
            .ToListAsync(ct);

        if (news.Count == 0)
            return Fail("The uploaded import has no new vouchers to activate (unknown import, or all already processed).");

        // Every distinct new fuel needs a cost > 0 (BulkAction's activate gate would otherwise refuse).
        var costByFuel = (command.Costs ?? new List<ExchangeFuelCost>())
            .Where(c => !string.IsNullOrWhiteSpace(c.FuelTypeId))
            .GroupBy(c => c.FuelTypeId)
            .ToDictionary(g => g.Key, g => g.Last().CostPerLiter);
        var newFuels = news.Select(n => n.FuelTypeId).Distinct().ToList();
        var missingCost = newFuels.Where(f => !costByFuel.TryGetValue(f, out var c) || c <= 0m).ToList();
        if (missingCost.Count > 0)
            return Fail($"Enter a cost per liter (> 0) for every new fuel. Missing: {string.Join(", ", missingCost)}.");

        var newIds = news.Select(n => n.Id).ToList();
        var now = DateTime.UtcNow;

        // InMemory doesn't support transactions; the reuse handlers share this scoped context, so one
        // outer transaction makes their internal SaveChanges flush (not commit) and we commit once.
        await using var tx = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(ct)
            : null;

        // Expire the olds BEFORE setting the new batch cost. SetBatchCost saves then recomputes the
        // blended pool from a fresh query, so with the olds already Expired (and flushed by that save)
        // the reprice lands on the post-exchange pool (new stock only) — no transient under-price,
        // and no second reprice pass that would collide on already-tracked packages.
        foreach (var v in olds)
        {
            v.Status = VoucherStatus.Expired;
            v.UpdatedAtUtc = now;
        }

        var blendedByFuel = new List<ExchangeFuelBlended>();
        foreach (var fuel in newFuels)
        {
            var costResult = await _setBatchCost.HandleAsync(
                new SetBatchCostCommand(command.NewImportId, fuel, costByFuel[fuel], command.ActingUserId, command.ActingUserName),
                ct);
            if (!costResult.Success)
                return Fail(costResult.Error ?? $"Failed to set the batch cost for fuel {fuel}.");
            blendedByFuel.Add(new ExchangeFuelBlended(fuel, costResult.BlendedCostPerLiter));
        }

        // Cost gate is now satisfied → activate the new vouchers (also kicks the fulfillment worker; benign).
        var activateResult = await _bulkAction.HandleAsync(
            new BulkActionVouchersCommand("activate", newIds, null, command.ActingUserId, command.ActingUserName),
            ct);
        if (!activateResult.Success)
            return Fail(activateResult.Error ?? "Failed to activate the new vouchers.");

        // Pair old→new flexibly, then write one exchange row per OLD voucher.
        var pairing = VoucherExchangePairing.Pair(
            olds.Select(o => new VoucherExchangePairing.Candidate(o.Id, o.Provider, o.FuelTypeId, o.Liters, o.VoucherNumber)).ToList(),
            news);

        var batchId = Guid.NewGuid();
        var oldById = olds.ToDictionary(o => o.Id);
        foreach (var (oldId, newId) in pairing.Pairs)
        {
            var old = oldById[oldId];
            _context.VoucherExchanges.Add(new VoucherExchange
            {
                Id = Guid.NewGuid(),
                ExchangeBatchId = batchId,
                OldVoucherId = oldId,
                NewVoucherId = newId,
                FuelTypeId = old.FuelTypeId,
                Provider = old.Provider,
                SurchargeUah = command.SurchargeUah,
                CostPerLiterApplied = costByFuel.TryGetValue(old.FuelTypeId, out var cpl) ? cpl : null,
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
