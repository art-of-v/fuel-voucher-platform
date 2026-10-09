using System.Globalization;
using System.Text.Json;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.PurchaseBatchCost;

/// <summary>
/// Enter/update the cost/liter for one batch (import × fuel). The rate is stamped onto the batch's vouchers
/// — cost lives per voucher, not per batch — and the fuel is then repriced off the new blended pool.
/// </summary>
/// <remarks>
/// <see cref="ForceOverwrite"/> exists because a voucher's cost is no longer a constant: a customer payment
/// reduces the cost of the one voucher they extended. A plain re-entry therefore only fills in vouchers that
/// have no cost yet, so correcting one batch's price cannot silently wipe out the money customers already paid.
/// Restamping everything is a deliberate, separate action.
/// </remarks>
public sealed record SetBatchCostCommand(
    Guid ImportJobId,
    string FuelTypeId,
    decimal CostPerLiter,
    Guid ActingUserId,
    string? ActingUserName,
    bool ForceOverwrite = false);

public sealed record SetBatchCostResult(
    bool Success,
    bool NotFound,
    string? Error,
    decimal? BlendedCostPerLiter,
    int PackagesRepriced,
    int VouchersCosted = 0);

public sealed class SetBatchCostCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly BlendedCostRecalculator _recalculator;
    private readonly ProviderEventService _eventService;
    private readonly NotificationDispatcher _notifications;

    public SetBatchCostCommandHandler(
        ApplicationDbContext context,
        BlendedCostRecalculator recalculator,
        ProviderEventService eventService,
        NotificationDispatcher notifications)
    {
        _context = context;
        _recalculator = recalculator;
        _eventService = eventService;
        _notifications = notifications;
    }

    public async Task<SetBatchCostResult> HandleAsync(SetBatchCostCommand command, CancellationToken cancellationToken = default)
    {
        if (command.CostPerLiter <= 0m)
            return new SetBatchCostResult(false, false, "Cost per liter must be greater than zero", null, 0);

        var importExists = await _context.VoucherImports.AnyAsync(i => i.Id == command.ImportJobId, cancellationToken);
        if (!importExists)
            return new SetBatchCostResult(false, true, "Import not found", null, 0);

        // The fuel must actually appear in this import; grab its provider to denormalize onto the batch.
        var provider = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .Where(v => v.ImportJobId == command.ImportJobId && v.FuelTypeId == command.FuelTypeId)
            .Select(v => v.Provider)
            .FirstOrDefaultAsync(cancellationToken);
        if (provider is null)
            return new SetBatchCostResult(false, true, "No vouchers for this fuel in the import", null, 0);

        var now = DateTime.UtcNow;
        var actingUser = command.ActingUserId == Guid.Empty ? (Guid?)null : command.ActingUserId;

        // Stamp the vouchers. Without ForceOverwrite this only fills vouchers that have no cost yet, because
        // an already-costed voucher carries a customer payment that must survive a price correction.
        var vouchers = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .Where(v => v.ImportJobId == command.ImportJobId && v.FuelTypeId == command.FuelTypeId)
            .ToListAsync(cancellationToken);

        var targets = command.ForceOverwrite
            ? vouchers
            : vouchers.Where(v => v.CostPerLiter is null).ToList();

        if (targets.Count == 0)
        {
            return new SetBatchCostResult(
                false, false,
                "This batch is already costed. Re-sending the price without overwrite changes nothing, because " +
                "the existing per-voucher costs already include what customers paid to extend them.",
                null, 0, 0);
        }

        var oldCostText = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .Where(v => v.ImportJobId == command.ImportJobId && v.FuelTypeId == command.FuelTypeId)
            .Select(v => v.CostPerLiter)
            .FirstOrDefaultAsync(cancellationToken);

        foreach (var v in targets)
        {
            v.CostPerLiter = command.CostPerLiter;
            v.UpdatedAtUtc = now;
        }
        _context.FuelVouchers.UpdateRange(targets);

        var batch = await _context.PurchaseBatches
            .FirstOrDefaultAsync(b => b.ImportJobId == command.ImportJobId && b.FuelTypeId == command.FuelTypeId, cancellationToken);
        if (batch is null)
        {
            // Import predates the supplier picker (or was created without one); borrow it from the vouchers
            // so the batch is never left without the counterparty we settle with.
            var supplierId = await _context.FuelVouchers
                .IgnoreQueryFilters()
                .Where(v => v.ImportJobId == command.ImportJobId && v.FuelTypeId == command.FuelTypeId)
                .Select(v => v.SupplierId)
                .FirstOrDefaultAsync(cancellationToken);

            if (supplierId is { } sid)
            {
                batch = new SharedKernel.Domain.PurchaseBatch
                {
                    Id = Guid.NewGuid(),
                    ImportJobId = command.ImportJobId,
                    FuelTypeId = command.FuelTypeId,
                    Provider = provider,
                    SupplierId = sid,
                    EnteredByUserId = actingUser,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                _context.PurchaseBatches.Add(batch);
            }
        }
        else
        {
            batch.Provider = provider;
            batch.EnteredByUserId = actingUser;
            batch.UpdatedAtUtc = now;
            _context.PurchaseBatches.Update(batch);
        }

        // Persist FIRST: production runs NoTracking, so the blended recompute below (a fresh query)
        // would not see the unsaved voucher costs otherwise.
        await _context.SaveChangesAsync(cancellationToken);

        var blended = await _recalculator.ComputeBlendedAsync(command.FuelTypeId, cancellationToken);
        var repriced = 0;
        if (blended is { } b)
        {
            repriced = await _recalculator.RepriceAsync(command.FuelTypeId, b, command.ActingUserId, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            // Slice-3 proactive alert: a rising blended cost may have pushed this supplier+fuel's
            // price below cost. If it did and no manager has opted it in, the hard block now refuses
            // every sale/activation — surface that immediately so pricing can be fixed.
            var fuelType = await _context.FuelTypes.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == command.FuelTypeId, cancellationToken);
            var pkg = await _context.FuelPackages.AsNoTracking()
                .FirstOrDefaultAsync(p => p.FuelTypeId == command.FuelTypeId, cancellationToken);
            if (fuelType is { AllowBelowCost: false } && pkg is not null)
            {
                var profit = pkg.MarginUahPerLiter ?? 0m;
                var minDiscount = pkg.MinDiscountPerLiter ?? 0m;
                if (FuelPricing.IsBelowCost(b, profit, pkg.PumpPricePerLiter, minDiscount))
                {
                    var finalPerLiter = FuelPricing.FinalPerLiter(b, profit, pkg.PumpPricePerLiter, minDiscount);
                    await _notifications.BelowCostAsync(provider, command.FuelTypeId, b, finalPerLiter, deliberate: false, cancellationToken);
                }
            }
        }

        if (actingUser is { } userId)
        {
            var oldText = oldCostText.HasValue ? oldCostText.Value.ToString("F2", CultureInfo.InvariantCulture) : "—";
            var newText = command.CostPerLiter.ToString("F2", CultureInfo.InvariantCulture);
            var blendedText = blended.HasValue ? blended.Value.ToString("F2", CultureInfo.InvariantCulture) : "—";
            // old_value/new_value are jsonb and every other audit event stores an OBJECT. Handing them a
            // bare decimal stores a JSON scalar instead: Postgres accepts it, so nothing fails loudly,
            // but the before/after is no longer machine-readable and the provider history has nothing
            // to render. This row is the only trace of what we paid — there is no supplier invoice to
            // fall back on — so it has to be as readable as the rest of the audit log.
            await _eventService.RecordEventAsync(
                "Batch",
                $"{command.ImportJobId}:{command.FuelTypeId}",
                "BatchCostEntered",
                oldCostText is { } previous
                    ? JsonSerializer.Serialize(new { FuelTypeId = command.FuelTypeId, CostPerLiter = previous })
                    : null,
                JsonSerializer.Serialize(new
                {
                    command.FuelTypeId,
                    command.CostPerLiter,
                    BlendedCostPerLiter = blended,
                    VouchersCosted = targets.Count,
                }),
                userId,
                command.ActingUserName,
                $"{provider} / {command.FuelTypeId}: cost {oldText} → {newText} UAH/L on {targets.Count} voucher(s); blended {blendedText}",
                provider,
                cancellationToken);
        }

        return new SetBatchCostResult(true, false, null, blended, repriced, targets.Count);
    }
}