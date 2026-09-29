using System.Globalization;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.PurchaseBatchCost;

/// <summary>Manually enter/update the cost/liter for one batch (import × fuel), then reprice off the new blended cost.</summary>
public sealed record SetBatchCostCommand(
    Guid ImportJobId,
    string FuelTypeId,
    decimal CostPerLiter,
    Guid ActingUserId,
    string? ActingUserName);

public sealed record SetBatchCostResult(
    bool Success,
    bool NotFound,
    string? Error,
    decimal? BlendedCostPerLiter,
    int PackagesRepriced);

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

        var batch = await _context.PurchaseBatches
            .FirstOrDefaultAsync(b => b.ImportJobId == command.ImportJobId && b.FuelTypeId == command.FuelTypeId, cancellationToken);
        var oldCost = batch?.CostPerLiter;
        var now = DateTime.UtcNow;
        var actingUser = command.ActingUserId == Guid.Empty ? (Guid?)null : command.ActingUserId;

        if (batch is null)
        {
            batch = new SharedKernel.Domain.PurchaseBatch
            {
                Id = Guid.NewGuid(),
                ImportJobId = command.ImportJobId,
                FuelTypeId = command.FuelTypeId,
                Provider = provider,
                CostPerLiter = command.CostPerLiter,
                EnteredByUserId = actingUser,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _context.PurchaseBatches.Add(batch);
        }
        else
        {
            batch.CostPerLiter = command.CostPerLiter;
            batch.Provider = provider;
            batch.EnteredByUserId = actingUser;
            batch.UpdatedAtUtc = now;
            _context.PurchaseBatches.Update(batch);
        }

        // Persist the batch FIRST: production runs NoTracking, so the blended recompute below
        // (a fresh query) would not see an unsaved add otherwise.
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
            var oldText = oldCost.HasValue ? oldCost.Value.ToString("F2", CultureInfo.InvariantCulture) : "—";
            var newText = command.CostPerLiter.ToString("F2", CultureInfo.InvariantCulture);
            var blendedText = blended.HasValue ? blended.Value.ToString("F2", CultureInfo.InvariantCulture) : "—";
            await _eventService.RecordEventAsync(
                "Batch",
                $"{command.ImportJobId}:{command.FuelTypeId}",
                "BatchCostEntered",
                oldCost?.ToString(CultureInfo.InvariantCulture),
                command.CostPerLiter.ToString(CultureInfo.InvariantCulture),
                userId,
                command.ActingUserName,
                $"{provider} / {command.FuelTypeId}: cost {oldText} → {newText} UAH/L; blended {blendedText}",
                provider,
                cancellationToken);
        }

        return new SetBatchCostResult(true, false, null, blended, repriced);
    }
}
