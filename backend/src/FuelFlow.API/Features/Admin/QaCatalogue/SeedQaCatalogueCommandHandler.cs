using System.Security.Cryptography;
using System.Text;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Admin.QaCatalogue;

/// <summary>
/// Mirrors the live catalogue into the rehearsal pool and tops its stock up, idempotently.
/// </summary>
/// <remarks>
/// <para>
/// A package is a fuel with a size, and the pool lives on the size (<see cref="FuelNominal"/>): the
/// storefront resolves one package per (fuel, litres) in the caller's pool, and the stock gate counts
/// vouchers with the same pool, brand, fuel and litres. So the whole seed is two writes - the size
/// twins, then the vouchers behind them - and their natural keys make a second run a no-op rather
/// than a multiplication.
/// </para>
/// <para>
/// The price is deliberately not mirrored: the margin and the supplier cost live on the fuel, which
/// both pools share, so a rehearsal package is priced exactly like its live counterpart. The pool
/// separates stock, not money.
/// </para>
/// <para>
/// The terms follow the live shape: a supplier term of 180 days against a customer term of 7. The
/// short customer term is what makes the near-expiry banner and the renewal entry point reachable
/// during a review session; a full-term voucher shows neither.
/// </para>
/// </remarks>
public sealed class SeedQaCatalogueCommandHandler
{
    private const int DefaultVouchersPerPackage = 10;
    private const int MaxVouchersPerPackage = 100;
    private const int SupplierTermDays = 180;
    private const int CustomerTermDays = 7;

    private readonly ApplicationDbContext _context;

    public SeedQaCatalogueCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<SeedQaCatalogueResult> HandleAsync(
        SeedQaCatalogueCommand command,
        CancellationToken cancellationToken = default)
    {
        var perPackage = Math.Clamp(
            command.VouchersPerPackage ?? DefaultVouchersPerPackage,
            1,
            MaxVouchersPerPackage);

        var now = SystemClock.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var live = await (
            from fuel in _context.FuelTypes.AsNoTracking()
            join brand in _context.Brands.AsNoTracking() on fuel.BrandId equals brand.Id
            join nominal in _context.FuelNominals.AsNoTracking() on fuel.Id equals nominal.FuelTypeId
            where !fuel.IsDeleted && nominal.DataKind == DataKinds.Real
            select new
            {
                FuelTypeId = fuel.Id,
                BrandId = brand.Id,
                BrandSlug = brand.Slug,
                SupplierPricePerLiter = fuel.SupplierPricePerLiter,
                nominal.Liters,
            })
            .ToListAsync(cancellationToken);

        var existingNominals = await _context.FuelNominals
            .Where(n => n.DataKind == DataKinds.Qa)
            .Select(n => new { n.FuelTypeId, n.Liters })
            .ToListAsync(cancellationToken);

        var haveNominal = existingNominals
            .Select(n => (n.FuelTypeId, n.Liters))
            .ToHashSet();

        var nominalsCreated = 0;
        foreach (var row in live)
        {
            if (haveNominal.Add((row.FuelTypeId, row.Liters)))
            {
                _context.FuelNominals.Add(new FuelNominal
                {
                    FuelTypeId = row.FuelTypeId,
                    DataKind = DataKinds.Qa,
                    Liters = row.Liters,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
                nominalsCreated++;
            }
        }

        // Every live rehearsal voucher for the packages in scope, so the series below can both count
        // what is still buyable and continue from the last number ever issued. A count alone cannot do
        // the second: deleting one row by hand leaves a gap, and re-issuing a number that already
        // exists trips the partial unique index on `voucher_number`.
        var existingStock = await _context.FuelVouchers
            .Where(v => v.DataKind == DataKinds.Qa && !v.IsDeleted)
            .Select(v => new { v.Brand, v.FuelTypeId, v.Liters, v.VoucherNumber, v.Status })
            .ToListAsync(cancellationToken);

        var vouchersCreated = 0;
        foreach (var row in live)
        {
            var brand = row.BrandSlug.ToLowerInvariant();
            var series = existingStock
                .Where(s => s.FuelTypeId == row.FuelTypeId
                         && s.Liters == row.Liters
                         && s.Brand == brand)
                .ToList();

            // Available, not total: a reviewer who buys ten packages should get ten more on the next
            // run rather than a silent no-op. Spent vouchers keep their numbers, which is why the new
            // ones start after the highest index already issued.
            var available = series.Count(s => s.Status == VoucherStatus.Available);
            var nextIndex = series.Select(s => TrailingIndex(s.VoucherNumber)).DefaultIfEmpty(0).Max() + 1;

            for (var created = available; created < perPackage; created++, nextIndex++)
            {
                var index = nextIndex;
                var key = $"{row.FuelTypeId}:{row.Liters}:{index}";
                var fingerprint = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8];

                _context.FuelVouchers.Add(new FuelVoucher
                {
                    Id = Guid.NewGuid(),
                    Brand = brand,
                    BrandId = row.BrandId,
                    FuelTypeId = row.FuelTypeId,
                    Liters = row.Liters,
                    CostPerLiter = row.SupplierPricePerLiter,
                    SupplierExpirationDate = today.AddDays(SupplierTermDays),
                    CustomerExpirationDate = today.AddDays(CustomerTermDays),
                    VoucherNumber = $"QA-CAT-{fingerprint}-{index:00}",
                    // Plaintext here; the value converter encrypts it on save and the hash is filled
                    // by QrPayloadHashValueGenerator, so this row is readable by the wallet exactly
                    // like an imported one.
                    QrPayload = $"QA-QR-{fingerprint}-{index:00}",
                    Status = VoucherStatus.Available,
                    DataKind = DataKinds.Qa,
                    AcquisitionSource = AcquisitionSources.Direct,
                    IsDeleted = false,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
                vouchersCreated++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new SeedQaCatalogueResult(
            nominalsCreated,
            vouchersCreated,
            live.Select(r => (r.FuelTypeId, r.Liters)).Distinct().Count(),
            perPackage);
    }

    /// <summary>
    /// The series number at the end of a voucher number in this pool, or 0 when it carries none.
    /// </summary>
    /// <remarks>
    /// Rows written by the retired SQL seeds share the numbering shape, so they count as members and
    /// the next number starts past them. A row with no trailing number is not part of any series and
    /// must not be read as "index 0" somewhere in the middle.
    /// </remarks>
    private static int TrailingIndex(string voucherNumber)
    {
        var dash = voucherNumber.LastIndexOf('-');
        return dash > 0 && int.TryParse(voucherNumber[(dash + 1)..], out var index) ? index : 0;
    }
}
