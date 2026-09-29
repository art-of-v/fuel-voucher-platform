using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.ImportStationNodes;

/// <summary>
/// Bulk-loads АЗК for the price radar. Upserts by id (explicit, else coordinate-derived) so a
/// re-import of the same brand file heals rows instead of duplicating them. Every row is validated
/// against the same rules as the single create; a bad row is reported, never silently dropped.
/// </summary>
public sealed class ImportStationNodesCommandHandler
{
    private readonly ApplicationDbContext _context;

    public ImportStationNodesCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<ImportStationNodesResult> HandleAsync(ImportStationNodesCommand command, CancellationToken ct = default)
    {
        var result = new ImportStationNodesResult();
        var parsed = StationNodeImportParser.Parse(command.Content, command.Format);
        result.Errors.AddRange(parsed.Errors);

        var brandIds = await _context.Stations.Select(s => s.Id).ToListAsync(ct);
        var brands = brandIds.ToHashSet(StringComparer.Ordinal);

        // Resolve + validate into a keyed map so a later row for the same node wins (last-wins).
        var pending = new Dictionary<string, StationNode>(StringComparer.Ordinal);
        foreach (var row in parsed.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.StationId) || string.IsNullOrWhiteSpace(row.Name))
            {
                result.Errors.Add(new StationNodeImportError(row.Line, "StationId and Name are required"));
                continue;
            }

            if (row.Lat is null || row.Lng is null ||
                !StationNodeCoordinates.IsValidLat(row.Lat.Value) ||
                !StationNodeCoordinates.IsValidLng(row.Lng.Value))
            {
                result.Errors.Add(new StationNodeImportError(row.Line, "Valid Lat (-90..90) and Lng (-180..180) are required"));
                continue;
            }

            if (!brands.Contains(row.StationId))
            {
                result.Errors.Add(new StationNodeImportError(row.Line, $"Station (brand) '{row.StationId}' does not exist"));
                continue;
            }

            var id = string.IsNullOrWhiteSpace(row.Id)
                ? StationNodeCoordinates.DeriveId(row.StationId, row.Lat.Value, row.Lng.Value)
                : row.Id!;

            pending[id] = new StationNode
            {
                Id = id,
                StationId = row.StationId,
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
        var existing = await _context.StationNodes
            .Where(n => ids.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, StringComparer.Ordinal, ct);

        var now = DateTime.UtcNow;
        foreach (var (id, incoming) in pending)
        {
            if (existing.TryGetValue(id, out var entity))
            {
                entity.StationId = incoming.StationId;
                entity.Name = incoming.Name;
                entity.Address = incoming.Address;
                entity.Phone = incoming.Phone;
                entity.City = incoming.City;
                entity.StationType = incoming.StationType;
                entity.Lat = incoming.Lat;
                entity.Lng = incoming.Lng;
                entity.UpdatedAtUtc = now;
                _context.StationNodes.Update(entity);
                result.Updated++;
            }
            else
            {
                incoming.CreatedAtUtc = now;
                incoming.UpdatedAtUtc = now;
                _context.StationNodes.Add(incoming);
                result.Added++;
            }
        }

        await _context.SaveChangesAsync(ct);
        return result;
    }
}
