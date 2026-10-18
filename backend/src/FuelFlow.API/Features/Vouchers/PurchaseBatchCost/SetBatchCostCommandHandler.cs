using System.Globalization;
using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.PurchaseBatchCost;

/// <summary>
/// Enter the cost/liter for one batch (import × fuel). The rate is stamped onto the batch's vouchers —
/// cost lives per voucher, not per batch — and the fuel is then repriced off the new blended pool.
/// </summary>
/// <remarks>
/// <para>
/// <b>A batch's price is stamped once and is not rewritten.</b> This used to carry a
/// <c>ForceOverwrite</c> flag for a deliberate "restamp everything": an exchange raises a voucher's cost by
/// its share of the surcharge (<see cref="VoucherCosting.AfterExchange"/>), so a plain re-entry would
/// refuse and an operator had no way to correct a price they had entered wrongly. The flag existed for
/// that, and it existed to <em>rewrite vouchers that were already bought</em> — which contradicts the
/// rule settled on 2026-10-17 and recorded in README (`purchase_batches`) and planning #360: what a lot
/// cost is a purchase fact and stays one.
/// </para>
/// <para>
/// It was also unreachable. The only caller,
/// <c>AdminVoucherImportController.SetBatchCost</c>, built this command without it and nothing in
/// <c>admin/</c> sent it, so the flag had no scenario behind it at all — it was a door with no handle,
/// waiting for the first caller to walk through it.
/// </para>
/// <para>
/// So it is gone. A price entered in error is corrected by a <b>new recorded entry</b>, not by rewriting
/// the old one, and the exchange uplift is the only thing that moves a voucher's cost after it is
/// stamped.
/// </para>
/// </remarks>
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
    int PackagesRepriced,
    int VouchersCosted = 0);

public sealed class SetBatchCostCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly BlendedCostRecalculator _recalculator;
    private readonly AuditEventService _eventService;
    private readonly NotificationDispatcher _notifications;

    public SetBatchCostCommandHandler(
        ApplicationDbContext context,
        BlendedCostRecalculator recalculator,
        AuditEventService eventService,
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

        // The fuel must actually appear in this import; grab its brand to denormalize onto the batch.
        // !IsDeleted for the same reason the readers filter it (#980): a deleted voucher is not
        // stock, so it cannot be the only thing that proves the fuel is in this import.
        var brand = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .Where(v => v.ImportJobId == command.ImportJobId && v.FuelTypeId == command.FuelTypeId && !v.IsDeleted)
            .Select(v => v.Brand)
            .FirstOrDefaultAsync(cancellationToken);
        if (brand is null)
            return new SetBatchCostResult(false, true, "No vouchers for this fuel in the import", null, 0);

        var now = DateTime.UtcNow;
        var actingUser = command.ActingUserId == Guid.Empty ? (Guid?)null : command.ActingUserId;

        // Stamp the vouchers. Without ForceOverwrite this only fills vouchers that have no cost yet, because
        // an already-costed voucher carries the uplift an exchange applied to it, and the operator can see
        // that uplift in the batch timeline.
        //
        // !IsDeleted: a deleted voucher is not stock and must not be re-costed (#980). Without it an
        // operator typing a cost writes it onto vouchers that do not exist, which then sit in the
        // pricing pool carrying a cost nobody should be paying.
        var vouchers = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .Where(v => v.ImportJobId == command.ImportJobId && v.FuelTypeId == command.FuelTypeId && !v.IsDeleted)
            .ToListAsync(cancellationToken);

        // Only vouchers that carry no cost yet. A batch is stamped once; what it cost is a purchase fact
        // and stays one (see the record's remarks on the removed ForceOverwrite flag). The one thing
        // that legitimately moves a voucher's cost afterwards is an exchange, which writes the uplift
        // itself.
        var targets = vouchers.Where(v => v.CostPerLiter is null).ToList();

        if (targets.Count == 0)
        {
            return new SetBatchCostResult(
                false, false,
                "This batch is already costed. Re-sending the price without overwrite changes nothing, because " +
                "the existing per-voucher costs already include what an exchange added to them.",
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
                    Id = Guid.CreateVersion7(),
                    ImportJobId = command.ImportJobId,
                    FuelTypeId = command.FuelTypeId,
                    Brand = brand,
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
            batch.Brand = brand;
            batch.EnteredByUserId = actingUser;
            batch.UpdatedAtUtc = now;
            _context.PurchaseBatches.Update(batch);
        }

        // Persist FIRST: production runs NoTracking, so the blended recompute below (a fresh query)
        // would not see the unsaved voucher costs otherwise.
        await _context.SaveChangesAsync(cancellationToken);

        // The arrival, so the recompute here is the operator's rule and not a side effect: the moving
        // average changes when a lot arrives and at no other time.
        var blended = await _recalculator.ComputeBlendedAsync(command.FuelTypeId, cancellationToken);
        var repriced = 0;
        if (blended is { } b)
        {
            repriced = await _recalculator.RepriceAsync(
                command.FuelTypeId, b, command.ActingUserId, OfferPriceReasons.BatchCost, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            // Slice-3 proactive alert: a rising blended cost may have pushed this supplier+fuel's
            // price below cost. If it did and no manager has opted it in, the hard block now refuses
            // every sale/activation — surface that immediately so pricing can be fixed.
            var fuelType = await _context.FuelTypes.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == command.FuelTypeId, cancellationToken);
            // The margin the new cost would be sold at belongs to the fuel, once - reading it off
            // "the first package of this fuel" was an arbitrary pick between rows that now cannot differ
            // at all, which is the point of the split. The fuel is already loaded for the opt-in flag
            // above, so the two answers come from one row.
            var offer = await _context.FuelOffers.AsNoTracking()
                .Where(o => o.FuelTypeId == command.FuelTypeId && o.DataKind == DataKinds.Real)
                .FirstOrDefaultAsync(cancellationToken);
            if (fuelType is { AllowBelowCost: false } && offer is not null)
            {
                var profit = fuelType.MarginUahPerLiter ?? 0m;
                var minDiscount = offer.MinDiscountPerLiter ?? 0m;
                if (FuelPricing.IsBelowCost(b, profit, offer.PumpPricePerLiter, minDiscount))
                {
                    var finalPerLiter = FuelPricing.FinalPerLiter(b, profit, offer.PumpPricePerLiter, minDiscount);
                    await _notifications.BelowCostAsync(brand, command.FuelTypeId, b, finalPerLiter, deliberate: false, cancellationToken);
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
            // but the before/after is no longer machine-readable and the brand history has nothing
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
                $"{brand} / {command.FuelTypeId}: cost {oldText} → {newText} UAH/L on {targets.Count} voucher(s); blended {blendedText}",
                brand,
                cancellationToken);
        }

        return new SetBatchCostResult(true, false, null, blended, repriced, targets.Count);
    }
}