using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands;

/// <summary>
/// Turns the network's own code — <c>"okko"</c>, as a supplier file, an invoice or a seed script
/// spells it — into the id the database stores, and back again.
/// </summary>
/// <remarks>
/// <para>
/// <b>Exists because the id stopped being the slug</b> (planning D-21). Every place that used to write
/// <c>BrandId = "okko"</c> now has to resolve first, and the alternative to a resolver is a query per
/// call site, which is how the parser ends up doing four lookups of the same four rows for four
/// vouchers.
/// </para>
/// <para>
/// <b>Scoped, not static, and deliberately.</b> The catalogue is a handful of rows that change when an
/// operator adds a network, so a process-wide cache would be a second source of truth with no
/// invalidation story. A scoped one costs one query per request at worst and cannot outlive the
/// request that read it. A long-running import pays it once per batch, which is the case that was
/// worth optimising.
/// </para>
/// <para>
/// <b>Archived brands are invisible.</b> The entity's query filter excludes them, so resolving a slug
/// whose brand was archived fails loudly rather than handing back a row the catalogue has stopped
/// selling. That is the correct answer for an import — an archived network should not accept new
/// stock — and the callers that genuinely need the archived row ask with
/// <c>IgnoreQueryFilters</c>.
/// </para>
/// </remarks>
public sealed class BrandSlugResolver
{
    private readonly ApplicationDbContext _context;
    private Dictionary<string, Guid>? _bySlug;
    private Dictionary<Guid, string>? _byId;

    public BrandSlugResolver(ApplicationDbContext context) => _context = context;

    /// <summary>The live networks, slug to id, matched case-insensitively.</summary>
    public async Task<Dictionary<string, Guid>> BySlugAsync(CancellationToken ct = default)
    {
        if (_bySlug is not null) return _bySlug;

        var rows = await _context.Brands.AsNoTracking()
            .Select(b => new { b.Slug, b.Id })
            .ToListAsync(ct);

        _bySlug = rows.ToDictionary(r => r.Slug, r => r.Id, StringComparer.OrdinalIgnoreCase);
        return _bySlug;
    }

    /// <summary>The live networks, id to slug.</summary>
    public async Task<Dictionary<Guid, string>> ByIdAsync(CancellationToken ct = default)
    {
        if (_byId is not null) return _byId;

        var rows = await _context.Brands.AsNoTracking()
            .Select(b => new { b.Slug, b.Id })
            .ToListAsync(ct);

        _byId = rows.ToDictionary(r => r.Id, r => r.Slug);
        return _byId;
    }

    /// <summary>
    /// The id of one network, or null when no live brand carries this slug.
    /// </summary>
    public async Task<Guid?> ResolveAsync(string slug, CancellationToken ct = default)
        => (await BySlugAsync(ct)).TryGetValue(slug, out var id) ? id : null;

    /// <summary>
    /// The slug of one network, or null when the id names no live brand.
    /// </summary>
    public async Task<string?> SlugOfAsync(Guid brandId, CancellationToken ct = default)
        => (await ByIdAsync(ct)).TryGetValue(brandId, out var slug) ? slug : null;
}
