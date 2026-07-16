using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.CreateStation;

public sealed class CreateStationCommandHandler
{
    private readonly ApplicationDbContext _context;

    public CreateStationCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<CreateStationResult> HandleAsync(CreateStationCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Station.Id) ||
            string.IsNullOrWhiteSpace(command.Station.Name) ||
            string.IsNullOrWhiteSpace(command.Station.LogoText))
            return new CreateStationResult { Conflict = false, Error = "Id, Name and LogoText are required", Success = false };

        var exists = await _context.Stations.AnyAsync(x => x.Id == command.Station.Id, ct);
        if (exists)
            return new CreateStationResult { Conflict = true, Error = $"Station with id '{command.Station.Id}' already exists" };

        command.Station.CreatedAtUtc = DateTime.UtcNow;
        _context.Stations.Add(command.Station);
        await _context.SaveChangesAsync(ct);
        return new CreateStationResult { Success = true, Station = command.Station };
    }
}

public sealed class CreateStationResult
{
    public bool Success { get; set; }
    public bool Conflict { get; set; }
    public string? Error { get; set; }
    public Station? Station { get; set; }
}
