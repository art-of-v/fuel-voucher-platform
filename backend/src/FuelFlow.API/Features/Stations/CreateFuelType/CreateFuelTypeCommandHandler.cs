using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.CreateFuelType;

public sealed class CreateFuelTypeCommandHandler
{
    private readonly ApplicationDbContext _context;
    public CreateFuelTypeCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<CreateFuelTypeResult> HandleAsync(CreateFuelTypeCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.FuelType.Id) ||
            string.IsNullOrWhiteSpace(command.FuelType.StationId) ||
            string.IsNullOrWhiteSpace(command.FuelType.Name))
            return new CreateFuelTypeResult { Error = "Id, StationId and Name are required" };

        var exists = await _context.FuelTypes.AnyAsync(x => x.Id == command.FuelType.Id, ct);
        if (exists)
            return new CreateFuelTypeResult { Conflict = true, Error = $"FuelType with id '{command.FuelType.Id}' already exists" };

        command.FuelType.CreatedAtUtc = DateTime.UtcNow;
        _context.FuelTypes.Add(command.FuelType);
        await _context.SaveChangesAsync(ct);
        return new CreateFuelTypeResult { Success = true, FuelType = command.FuelType };
    }
}

public sealed class CreateFuelTypeResult
{
    public bool Success { get; set; }
    public bool Conflict { get; set; }
    public string? Error { get; set; }
    public FuelTypeEntity? FuelType { get; set; }
}
