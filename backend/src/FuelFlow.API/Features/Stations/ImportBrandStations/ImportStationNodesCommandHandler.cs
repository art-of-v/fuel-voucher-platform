using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.ImportBrandStations;

/// <summary>
/// Bulk-loads АЗК for the price radar. Upserts by id (explicit, else coordinate-derived) so a
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

        var brandIds = await _context.Brands.Select(s => s.Id).ToListAsync(ct);
        var brands = brandIds.ToHashSet(StringComparer.Ordinal);

        // Resolve + validate into a keyed map so a later row for the same node wins (last-wins).
        var pending = new Dictionary<string, BrandStation>(StringComparer.Ordinal);
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

            if (!brands.Contains(row.BrandId))
            {
                result.Errors.Add(new BrandStationImportError(row.Line, $"Brand (brand) '{row.BrandId}' does not exist"));
                continue;
            }

            var id = string.IsNullOrWhiteSpace(row.Id)
                ? BrandStationCoordinates.DeriveId(row.BrandId, row.Lat.Value, row.Lng.Value)
                : row.Id!;

            pending[id] = new BrandStation
            {
                Id = id,
                BrandId = row.BrandId,
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

        var ids = pending.Keys.ToList();
        var existing = await _context.BrandStations
            .Where(n => ids.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, StringComparer.Ordinal, ct);

        var now = DateTime.UtcNow;
        foreach (var (id, incoming) in pending)
        {
            if (existing.TryGetValue(id, out var entity))
            {
                entity.BrandId = incoming.BrandId;
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
