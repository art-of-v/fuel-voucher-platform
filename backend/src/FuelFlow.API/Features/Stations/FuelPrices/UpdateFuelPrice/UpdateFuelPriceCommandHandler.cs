using FuelFlow.Features.Stations.FuelPrices.GetFuelPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.FuelPrices.UpdateFuelPrice;

public sealed class UpdateFuelPriceCommandHandler
{
    private readonly ApplicationDbContext _context;

    public UpdateFuelPriceCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<(FuelPriceDto? Result, string? Error)> HandleAsync(
        UpdateFuelPriceCommand command, CancellationToken ct = default)
    {
        var entity = await _context.FuelPackages
            .FirstOrDefaultAsync(p => p.Id == command.PackageId, ct);

        if (entity is null)
            return (null, "Package not found");

        // Validate & recalculate
        var (supplier, marginUah, marginPct, finalPrice, error) = Recalculate(
            command.SupplierPricePerLiter ?? entity.SupplierPricePerLiter,
            command.MarginUahPerLiter,
            command.MarginPercent,
            command.FinalPricePerLiter
        );

        if (error is not null)
            return (null, error);

        // Create audit record
        var audit = new FuelPackagePriceAudit
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
            ChangedAtUtc = DateTime.UtcNow
        };

        entity.SupplierPricePerLiter = supplier;
        entity.MarginUahPerLiter = marginUah;
        entity.MarginPercent = marginPct;
        entity.FinalPricePerLiter = finalPrice;
        entity.PriceUpdatedAt = DateTime.UtcNow;
        entity.PriceUpdatedByUserId = command.ChangedByUserId;

        // Sync the legacy total-price fields (in kopecks) for frontends that divide by 100
        if (finalPrice.HasValue)
        {
            entity.Price = (int)Math.Round(finalPrice.Value * entity.Liters * 100m);
        }
        if (supplier.HasValue)
        {
            entity.OriginalPrice = (int)Math.Round(supplier.Value * entity.Liters * 100m);
        }

        _context.FuelPackagePriceAudits.Add(audit);
        await _context.SaveChangesAsync(ct);

        var stationName = await _context.Stations
            .AsNoTracking()
            .Where(s => s.Id == entity.StationId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct) ?? entity.StationId;

        return (new FuelPriceDto
        {
            Id = entity.Id,
            StationId = entity.StationId,
            StationName = stationName,
            FuelTypeId = entity.FuelTypeId,
            FuelName = entity.FuelName,
            Liters = entity.Liters,
            SupplierPricePerLiter = entity.SupplierPricePerLiter,
            MarginUahPerLiter = entity.MarginUahPerLiter,
            MarginPercent = entity.MarginPercent,
            FinalPricePerLiter = entity.FinalPricePerLiter,
            PriceUpdatedAt = entity.PriceUpdatedAt,
            PriceUpdatedByUserId = entity.PriceUpdatedByUserId,
        }, null);
    }

    /// <summary>
    /// Server-side recalculation. Priority: FinalPrice > MarginUah > MarginPercent.
    /// Supplier is always required if any margin is set.
    /// </summary>
    internal static (decimal? supplier, decimal? marginUah, decimal? marginPct, decimal? finalPrice, string? error)
        Recalculate(decimal? supplier, decimal? marginUah, decimal? marginPct, decimal? finalPrice)
    {
        // Validation
        if (supplier.HasValue && supplier.Value < 0)
            return (null, null, null, null, "Supplier price cannot be negative");

        if (finalPrice.HasValue && finalPrice.Value < 0)
            return (null, null, null, null, "Final price cannot be negative");

        if (marginPct.HasValue && marginPct.Value > 500)
            return (null, null, null, null, "Margin % cannot exceed 500%");

        if (!supplier.HasValue)
            return (supplier, marginUah, marginPct, finalPrice, null);

        decimal sup = supplier.Value;

        // Priority: if finalPrice was given, derive margin
        if (finalPrice.HasValue)
        {
            decimal fin = finalPrice.Value;
            decimal mUah = fin - sup;
            decimal mPct = sup != 0 ? Math.Round(mUah / sup * 100, 4) : 0;
            return (sup, mUah, mPct, fin, null);
        }

        // If marginUah was given
        if (marginUah.HasValue)
        {
            decimal mUah = marginUah.Value;
            decimal fin = sup + mUah;
            decimal mPct = sup != 0 ? Math.Round(mUah / sup * 100, 4) : 0;
            if (fin < 0) return (null, null, null, null, "Final price cannot be negative");
            return (sup, mUah, mPct, fin, null);
        }

        // If marginPercent was given
        if (marginPct.HasValue)
        {
            decimal mUah = Math.Round(sup * marginPct.Value / 100, 4);
            decimal fin = sup + mUah;
            if (fin < 0) return (null, null, null, null, "Final price cannot be negative");
            return (sup, mUah, marginPct.Value, fin, null);
        }

        // Only supplier changed: preserve existing margin if possible
        return (sup, marginUah, marginPct, finalPrice, null);
    }
}
