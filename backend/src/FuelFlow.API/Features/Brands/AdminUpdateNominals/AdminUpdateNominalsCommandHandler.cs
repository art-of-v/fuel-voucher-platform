using System.Text.Json;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.AdminUpdateNominals;

/// <summary>
/// Rewrites the sizes every fuel of a network is sold in, keeping the rows that survive.
/// </summary>
/// <remarks>
/// The sizes a fuel is sold in are the operator's answer, written once per fuel and pool rather
/// than once per offer. The per-volume loops that used to follow are gone:
/// <c>fuel_offer_volumes</c> was a second copy of this same answer, and keeping both in step was
/// the whole cost of having it.
/// </remarks>
public sealed class AdminUpdateNominalsCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;

    public AdminUpdateNominalsCommandHandler(ApplicationDbContext context, AuditEventService eventService)
    {
        _context = context;
        _eventService = eventService;
    }

    public async Task<AdminUpdateNominalsResult> HandleAsync(AdminUpdateNominalsCommand command, CancellationToken ct = default)
    {
        var nominals = command.Nominals;

        // Rejected here rather than left to ck_fuel_packages_liters_positive. The request body binds
        // List<int> straight off the wire, so -10 used to create a package priced at -10 x the
        // per-litre rate and the storefront listed it. A 23514 from the database would be correct but
        // opaque - it names neither the offending nominal nor this endpoint - so the caller gets a
        // message that says which value and why.
        var invalid = nominals.Where(n => n <= 0).Distinct().OrderBy(n => n).ToList();
        if (invalid.Count > 0)
        {
            return new AdminUpdateNominalsResult
            {
                Error = "A nominal volume must be a positive number of litres.",
                Code = BrandRefusalCodes.NominalsInvalid,
                Invalid = invalid
            };
        }

        var Brand = await _context.Brands.FirstOrDefaultAsync(s => s.Id == command.BrandId, ct);
        if (Brand is null) return new AdminUpdateNominalsResult { NotFound = true };

        var BrandFuels = await _context.FuelTypes.Where(f => f.BrandId == command.BrandId).ToListAsync(ct);
        var fuelIds = BrandFuels.Select(f => f.Id).ToList();

        // The sizes a fuel is sold in are the operator's answer, written once per fuel and pool rather than
        // once per offer. Read AsTracking: the rows below are updated in place, and the context defaults to
        // NoTracking - without it the edit would persist nothing (NoTrackingMutationGuardTests).
        var existingNominals = await _context.FuelNominals
            .AsTracking()
            .Where(n => fuelIds.Contains(n.FuelTypeId))
            .ToListAsync(ct);

        var oldValue = JsonSerializer.Serialize(
            existingNominals.Select(n => (int)n.Liters).Distinct().OrderBy(l => l).ToList());

        // #217: keep what survives, archive what leaves, add what is new.
        //
        // This used to RemoveRange every package and recreate the whole set with fresh ids. Two
        // things were wrong with that: order_line_items point at packages, so removing one took the
        // record of what a customer bought with it, and a fresh id on every price edit meant the
        // package an order referenced was never the package in the catalog. Reusing a package that
        // stays keeps its id, so historical rows keep resolving to the thing they bought.
        //
        // Pricing is unchanged: the cost, the column price and the margin are all the fuel's, and only
        // the derived package total is rewritten on each volume.
        var wanted = nominals.Distinct().ToHashSet();
        var now = SystemClock.UtcNow;

        // The sizes a fuel is sold in are the operator's answer, written once per fuel and pool. The
        // per-volume loops that used to follow are gone: `fuel_offer_volumes` was a second copy of this
        // same answer, and keeping both in step was the whole cost of having it.
        foreach (var fuel in BrandFuels)
        {
            foreach (var leaving in existingNominals.Where(n => n.FuelTypeId == fuel.Id
                                                             && n.DataKind == DataKinds.Real
                                                             && !wanted.Contains((int)n.Liters)))
            {
                _context.FuelNominals.Remove(leaving);
            }

            foreach (var liters in wanted.Where(l => existingNominals.All(
                         n => n.FuelTypeId != fuel.Id || n.DataKind != DataKinds.Real || n.Liters != l)))
            {
                // The brand screen edits the live catalogue. The rehearsal one is seeded by the QA
                // scripts, exactly as it was before the sizes moved onto the fuel.
                _context.FuelNominals.Add(new FuelNominal
                {
                    FuelTypeId = fuel.Id,
                    DataKind = DataKinds.Real,
                    Liters = liters,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
        }

        var newValue = JsonSerializer.Serialize(nominals.OrderBy(n => n).ToList());
        _eventService.RecordEvent(
            "Nominal", command.BrandId.ToString(), "NominalSetChanged",
            oldValue, newValue, command.UserId, command.UserName,
            $"{Brand.Name}: nominals changed [{string.Join(", ", nominals.OrderBy(n => n))}]",
            Brand.Slug);

        // One save for the sizes and the audit row: the row and the change it describes commit
        // together, or neither does (planning #533).
        await _context.SaveChangesAsync(ct);

        return new AdminUpdateNominalsResult { Success = true };
    }
}
