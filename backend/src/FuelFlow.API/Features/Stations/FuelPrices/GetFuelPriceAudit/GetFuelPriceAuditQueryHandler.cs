using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.FuelPrices.GetFuelPriceAudit;

public sealed class GetFuelPriceAuditQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetFuelPriceAuditQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<FuelPriceAuditDto>> HandleAsync(GetFuelPriceAuditQuery query, CancellationToken ct = default)
    {
        var q = _context.FuelPackagePriceAudits.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.PackageId))
            q = q.Where(a => a.PackageId == query.PackageId);

        return await q
            .OrderByDescending(a => a.ChangedAtUtc)
            .Take(query.Limit)
            .Select(a => new FuelPriceAuditDto
            {
                Id = a.Id,
                PackageId = a.PackageId,
                FuelName = a.FuelName,
                OldSupplierPricePerLiter = a.OldSupplierPricePerLiter,
                NewSupplierPricePerLiter = a.NewSupplierPricePerLiter,
                OldMarginUahPerLiter = a.OldMarginUahPerLiter,
                NewMarginUahPerLiter = a.NewMarginUahPerLiter,
                OldMarginPercent = a.OldMarginPercent,
                NewMarginPercent = a.NewMarginPercent,
                OldFinalPricePerLiter = a.OldFinalPricePerLiter,
                NewFinalPricePerLiter = a.NewFinalPricePerLiter,
                ChangedByUserId = a.ChangedByUserId,
                ChangedAtUtc = a.ChangedAtUtc
            })
            .ToListAsync(ct);
    }
}

public sealed class FuelPriceAuditDto
{
    public Guid Id { get; set; }
    public string PackageId { get; set; } = null!;
    public string FuelName { get; set; } = null!;
    public decimal? OldSupplierPricePerLiter { get; set; }
    public decimal? NewSupplierPricePerLiter { get; set; }
    public decimal? OldMarginUahPerLiter { get; set; }
    public decimal? NewMarginUahPerLiter { get; set; }
    public decimal? OldMarginPercent { get; set; }
    public decimal? NewMarginPercent { get; set; }
    public decimal? OldFinalPricePerLiter { get; set; }
    public decimal? NewFinalPricePerLiter { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}
