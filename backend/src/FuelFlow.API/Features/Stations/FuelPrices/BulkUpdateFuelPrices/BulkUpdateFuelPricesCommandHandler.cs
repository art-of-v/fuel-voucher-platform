using FuelFlow.Features.Stations.FuelPrices.UpdateFuelPrice;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.FuelPrices.BulkUpdateFuelPrices;

public sealed class BulkUpdateFuelPricesCommandHandler
{
    private readonly ApplicationDbContext _context;

    public BulkUpdateFuelPricesCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<BulkUpdateResult> HandleAsync(BulkUpdateFuelPricesCommand command, CancellationToken ct = default)
    {
        var packageIds = command.Patches.Select(p => p.PackageId).ToList();

        var entities = await _context.FuelPackages
            .Where(p => packageIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var errors = new List<BulkUpdateError>();
        var audits = new List<FuelPackagePriceAudit>();
        var now = DateTime.UtcNow;

        foreach (var patch in command.Patches)
        {
            if (!entities.TryGetValue(patch.PackageId, out var entity))
            {
                errors.Add(new BulkUpdateError(patch.PackageId, "Package not found"));
                continue;
            }

            var (supplier, marginUah, marginPct, finalPrice, error) = UpdateFuelPriceCommandHandler.Recalculate(
                patch.SupplierPricePerLiter ?? entity.SupplierPricePerLiter,
                patch.MarginUahPerLiter,
                patch.MarginPercent,
                patch.FinalPricePerLiter
            );

            if (error is not null)
            {
                errors.Add(new BulkUpdateError(patch.PackageId, error));
                continue;
            }

            audits.Add(new FuelPackagePriceAudit
            {
                Id = Guid.NewGuid(),
                PackageId = entity.Id,
                FuelName = entity.FuelName,
                OldSupplierPricePerLiter = entity.SupplierPricePerLiter,
                NewSupplierPricePerLiter = supplier,
                OldMarginUahPerLiter = entity.MarginUahPerLiter,
                NewMarginUahPerLiter = marginUah,
                OldMarginPercent = entity.MarginPercent,
                NewMarginPercent = marginPct,
                OldFinalPricePerLiter = entity.FinalPricePerLiter,
                NewFinalPricePerLiter = finalPrice,
                ChangedByUserId = command.ChangedByUserId,
                ChangedAtUtc = now
            });

            entity.SupplierPricePerLiter = supplier;
            entity.MarginUahPerLiter = marginUah;
            entity.MarginPercent = marginPct;
            entity.FinalPricePerLiter = finalPrice;
            entity.PriceUpdatedAt = now;
            entity.PriceUpdatedByUserId = command.ChangedByUserId;

            if (finalPrice.HasValue)
                entity.Price = (int)Math.Round(finalPrice.Value * entity.Liters);
            if (supplier.HasValue)
                entity.OriginalPrice = (int)Math.Round(supplier.Value * entity.Liters);
        }

        if (audits.Count > 0)
        {
            _context.FuelPackagePriceAudits.AddRange(audits);
            await _context.SaveChangesAsync(ct);
        }

        return new BulkUpdateResult(audits.Count, errors);
    }
}

public sealed record BulkUpdateResult(int UpdatedCount, List<BulkUpdateError> Errors);
public sealed record BulkUpdateError(string PackageId, string Message);
