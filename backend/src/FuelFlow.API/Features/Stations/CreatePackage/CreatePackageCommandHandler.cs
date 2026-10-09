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

        // One supplier price per fuel. Every other write path copies the cost off an existing package
        // (ProvidersController seeds, prices and station creation), so this endpoint is the only place
        // where a package can arrive carrying a cost of its own — and that is how a 0.01 L test
        // package at 5.00/L ended up beside 100.00/L packages, after which the fuel card reported it
        // healthy while the below-cost guard refused to sell the real fuel at all.
        //
        // Being the owner is not a way around a business rule, so this refuses rather than warns: the
        // caller has to price the fuel as a whole (UpdateFuel writes one cost to every package) or put
        // test fuel in its own fuel type.
        var existingCosts = await _context.FuelPackages
            .Where(p => p.FuelTypeId == command.Package.FuelTypeId && p.SupplierPricePerLiter != null)
            .Select(p => new { p.Liters, p.SupplierPricePerLiter })
            .ToListAsync(ct);

        if (command.Package.SupplierPricePerLiter is { } incoming && existingCosts.Count > 0)
        {
            // Rounded to the column's scale so a value that only differs by representation cannot trip
            // the rule: the guard must refuse a real disagreement, not a rounding artefact.
            var expected = decimal.Round(existingCosts[0].SupplierPricePerLiter!.Value, 4);
            var offered = decimal.Round(incoming, 4);

            if (offered != expected)
            {
                var held = string.Join(", ", existingCosts
                    .OrderBy(c => c.Liters)
                    .Select(c => $"{decimal.Round(c.SupplierPricePerLiter!.Value, 4):0.####} at {c.Liters:0.##} L"));

                return new CreatePackageResult
                {
                    Conflict = true,
                    Error = $"This fuel already has a supplier price of {expected:0.####} UAH/L ({held}). "
                        + $"A package cannot arrive with a different cost of {offered:0.####} UAH/L: the fuel card reads one price "
                        + "for the whole fuel while the below-cost guard enforces it per package, so they would disagree. "
                        + "Price the fuel as a whole instead, or give test fuel its own fuel type."
                };
            }
        }

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