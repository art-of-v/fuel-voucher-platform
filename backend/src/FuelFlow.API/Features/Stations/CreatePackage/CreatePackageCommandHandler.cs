using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.CreatePackage;

public sealed class CreatePackageCommandHandler
{
    private readonly ApplicationDbContext _context;
    public CreatePackageCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<CreatePackageResult> HandleAsync(CreatePackageCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Package.Id) ||
            string.IsNullOrWhiteSpace(command.Package.StationId) ||
            string.IsNullOrWhiteSpace(command.Package.FuelTypeId))
            return new CreatePackageResult { Error = "Id, StationId and FuelTypeId are required" };

        var exists = await _context.FuelPackages.AnyAsync(x => x.Id == command.Package.Id, ct);
        if (exists)
            return new CreatePackageResult { Conflict = true, Error = $"Package with id '{command.Package.Id}' already exists" };

        command.Package.CreatedAtUtc = DateTime.UtcNow;
        _context.FuelPackages.Add(command.Package);
        await _context.SaveChangesAsync(ct);
        return new CreatePackageResult { Success = true, Package = command.Package };
    }
}

public sealed class CreatePackageResult
{
    public bool Success { get; set; }
    public bool Conflict { get; set; }
    public string? Error { get; set; }
    public FuelPackage? Package { get; set; }
}
