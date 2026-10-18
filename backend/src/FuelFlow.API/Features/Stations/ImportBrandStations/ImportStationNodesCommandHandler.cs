using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.ImportBrandStations;

/// <summary>
/// Bulk-loads АЗК for the price radar. Upserts by (brand, external id) — the explicit id the file
/// supplies, else the one <see cref="BrandStationCoordinates"/> derives from the coordinates — so a
/// re-import of the same brand file heals rows instead of duplicating them. Every row is validated
/// against the same rules as the single create; a bad row is reported, never silently dropped.
/// </summary>
public sealed class ImportBrandStationsCommandHandler
{
    private readonly ApplicationDbContext _context;

    public ImportBrandStationsCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<ImportBrandStationsResult> HandleAsync(ImportBrandStationsCommand command, CancellationToken ct = default)
    {
        var result = new ImportBrandStationsResult();
        var parsed = BrandStationImportParser.Parse(command.Content, command.Format);
        result.Errors.AddRange(parsed.Errors);

        // Slug -> id: the file names its network the way a supplier does, and the database no longer
        // stores that as the id. Archived rows are excluded by the entity's query filter, so a
        // re-import heals nothing that was deliberately archived and adds it back fresh instead.
        var brandIdsBySlug = await _context.Brands
            .AsNoTracking()
            .ToDictionaryAsync(b => b.Slug, b => b.Id, StringComparer.OrdinalIgnoreCase, ct);

        // Resolve + validate into a keyed map so a later row for the same node wins (last-wins).
        var pending = new Dictionary<(Guid BrandId, string ExternalId), BrandStation>();
        foreach (var row in parsed.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.BrandId) || string.IsNullOrWhiteSpace(row.Name))
            {
                result.Errors.Add(new BrandStationImportError(row.Line, "BrandId and Name are required"));
                continue;
            }

            if (row.Lat is null || row.Lng is null ||
                !BrandStationCoordinates.IsValidLat(row.Lat.Value) ||
                !BrandStationCoordinates.IsValidLng(row.Lng.Value))
            {
                result.Errors.Add(new BrandStationImportError(row.Line, "Valid Lat (-90..90) and Lng (-180..180) are required"));
                continue;
            }

            if (!brandIdsBySlug.TryGetValue(row.BrandId, out var brandId))
            {
                result.Errors.Add(new BrandStationImportError(row.Line, $"Brand (brand) '{row.BrandId}' does not exist"));
                continue;
            }

            var externalId = string.IsNullOrWhiteSpace(row.Id)
                ? BrandStationCoordinates.DeriveId(row.BrandId, row.Lat.Value, row.Lng.Value)
                : row.Id!;

            pending[(brandId, externalId)] = new BrandStation
            {
                ExternalId = externalId,
                BrandId = brandId,
                Name = row.Name,
                Address = row.Address,
                Phone = row.Phone,
                City = row.City,
                StationType = row.StationType,
                Lat = row.Lat,
                Lng = row.Lng,
            };
        }

        if (pending.Count == 0) return result;

        var pairs = pending.Keys.ToList();
        var brandFilter = pairs.Select(p => p.BrandId).Distinct().ToList();
        var externalFilter = pairs.Select(p => p.ExternalId).Distinct().ToList();
        var existing = await _context.BrandStations
            .Where(n => brandFilter.Contains(n.BrandId) && externalFilter.Contains(n.ExternalId))
            .ToListAsync(ct);
        var existingByKey = existing.ToDictionary(n => (n.BrandId, n.ExternalId));

        var now = DateTime.UtcNow;
        foreach (var (key, incoming) in pending)
        {
            if (existingByKey.TryGetValue(key, out var entity))
            {
                entity.Name = incoming.Name;
                entity.Address = incoming.Address;
                entity.Phone = incoming.Phone;
                entity.City = incoming.City;
                entity.StationType = incoming.StationType;
                entity.Lat = incoming.Lat;
                entity.Lng = incoming.Lng;
                entity.UpdatedAtUtc = now;
                _context.BrandStations.Update(entity);
                result.Updated++;
            }
            else
            {
                incoming.CreatedAtUtc = now;
                incoming.UpdatedAtUtc = now;
                _context.BrandStations.Add(incoming);
                result.Added++;
            }
        }

        await _context.SaveChangesAsync(ct);
        return result;
    }
}
