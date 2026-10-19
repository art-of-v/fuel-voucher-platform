using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests;

/// <summary>
/// The four seeded networks, as rows.
/// </summary>
/// <remarks>
/// <para>
/// A test that points a fuel, an offer or a voucher at <see cref="BrandIds"/> used to be complete on
/// its own, because the brand's id <em>was</em> its name and nothing had to exist for the name to
/// resolve. Since planning D-21 the row is a real row, and the read paths that key on a brand —
/// <c>BulkCheckoutCommandHandler</c>'s id→slug map, the public package projection's join for the
/// slug — find nothing when it is absent. The failure is a <c>KeyNotFoundException</c> eighty tests
/// deep, so every fixture that names a brand calls this once instead.
/// </para>
/// <para>
/// Idempotent, because most of these tests build their context per test and several seed the same
/// network twice.
/// </para>
/// </remarks>
internal static class SeededBrands
{
    /// <summary>
/// Seeds any of the four that are not already present, and saves. Cheap; safe to call
/// unconditionally. The save is load-bearing: the read paths that fail are queries, not tracker
/// walks, and a row only a query cannot see is the same as no row at all.
/// </summary>
    public static void AddMissing(this ApplicationDbContext context, params Guid[] brandIds)
    {
        var wanted = brandIds.Length > 0 ? brandIds : new[] { BrandIds.Okko, BrandIds.Wog, BrandIds.Upg, BrandIds.Klo };
        var present = context.Brands.IgnoreQueryFilters().Select(b => b.Id).ToList();
        var now = DateTime.UtcNow;
        var added = 0;

        foreach (var id in wanted.Distinct().Except(present))
        {
            context.Brands.Add(new Brand
            {
                Id = id,
                Slug = SlugOf(id),
                Name = NameOf(id),
                LogoText = NameOf(id),
                Color = "#00ff80",
                SortOrder = 999,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            added++;
        }

        if (added > 0) context.SaveChanges();
    }

    private static string SlugOf(Guid id) => id switch
    {
        _ when id == BrandIds.Okko => "okko",
        _ when id == BrandIds.Wog => "wog",
        _ when id == BrandIds.Upg => "upg",
        _ when id == BrandIds.Klo => "klo",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Not one of the four seeded networks.")
    };

    private static string NameOf(Guid id) => id switch
    {
        _ when id == BrandIds.Okko => "OKKO",
        _ when id == BrandIds.Wog => "WOG",
        _ when id == BrandIds.Upg => "UPG",
        _ when id == BrandIds.Klo => "KLO",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Not one of the four seeded networks.")
    };
}