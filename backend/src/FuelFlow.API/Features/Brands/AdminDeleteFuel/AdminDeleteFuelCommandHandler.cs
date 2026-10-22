using System.Text.Json;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.AdminDeleteFuel;

/// <summary>
/// Archives a fuel - but only once every voucher sold against it has been spent or expired.
/// </summary>
/// <remarks>
/// The refusal is the whole point of the handler. A fuel archived while customers still hold its
/// vouchers leaves those vouchers pointing at a row the catalogue no longer shows, so they cannot
/// be priced (#217). Refusing here is the cheap place to catch it - after the fact, the vouchers
/// are already unusable and the only repair is un-archiving and reconciling by hand.
/// </remarks>
public sealed class AdminDeleteFuelCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;

    public AdminDeleteFuelCommandHandler(ApplicationDbContext context, AuditEventService eventService)
    {
        _context = context;
        _eventService = eventService;
    }

    public async Task<AdminDeleteFuelResult> HandleAsync(AdminDeleteFuelCommand command, CancellationToken ct = default)
    {
        // Read AsTracking: the row below is archived in place, and without it the archive would be
        // dropped by the save (NoTrackingMutationGuardTests).
        var fuel = await _context.FuelTypes
            .AsTracking()
            .FirstOrDefaultAsync(f => f.Id == command.FuelId, ct);
        if (fuel is null) return new AdminDeleteFuelResult { NotFound = true };

        // A fuel may only be archived once it is idle: every one of its vouchers spent or expired.
        //
        // Settled with the operator, 2026-10-17. The two conditions are separate and both matter. The
        // brand dropping a fuel says it is no longer sold, which only the price sheet can establish —
        // that half is `ImportOkkoCatalogueCommandHandler`'s to prove, and it never guesses. This half
        // is the one that protects the money: a fuel archived while customers still hold its vouchers
        // leaves those vouchers pointing at a row the catalogue no longer shows, so they cannot be
        // priced (#217). Refusing here is the cheap place to catch it — after the fact, the vouchers
        // are already unusable and the only repair is un-archiving and reconciling by hand.
        var blockingStatuses = VoucherStatuses.Blocking;
        var liveVouchers = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .Where(v => v.FuelTypeId == command.FuelId && !v.IsDeleted && blockingStatuses.Contains(v.Status))
            .Select(v => new { v.Id, v.Status, v.VoucherNumber, v.Liters })
            .Take(6)
            .ToListAsync(ct);

        if (liveVouchers.Count > 0)
        {
            var total = await _context.FuelVouchers
                .IgnoreQueryFilters()
                .CountAsync(v => v.FuelTypeId == command.FuelId && !v.IsDeleted && blockingStatuses.Contains(v.Status), ct);

            return new AdminDeleteFuelResult
            {
                Conflict = true,
                Error = $"«{fuel.Name}» не можна архівувати: {total} талон(ів) ще активні. " +
                        "Архівувати можна лише пальне, у якого всі талони використані або прострочені.",
                ActiveVoucherCount = total,
                Examples = liveVouchers
                    .Select(v => new LiveVoucherExample(v.VoucherNumber, v.Status.ToString(), v.Liters))
                    .ToList()
            };
        }

        var Brand = await _context.Brands.FirstOrDefaultAsync(s => s.Id == fuel.BrandId, ct);
        var BrandName = Brand?.Name ?? "?";
        var fuelName = fuel.Name;

        // Archive rather than remove - same reasoning as the brand branch above: the cascade from
        // fuel_types reaches fuel_vouchers (#217). The sizes are not archived either: they belong to
        // the fuel, and this row is the one that says no. It used to be a second row doing that job,
        // and a live price row put the fuel back on the shelf the moment a nominals edit touched it.
        var archivedAt = SystemClock.UtcNow;

        fuel.IsDeleted = true;
        fuel.DeletedAtUtc = archivedAt;

        _eventService.RecordEvent(
            "Fuel", command.FuelId.ToString(), "FuelRemoved",
            JsonSerializer.Serialize(new { fuelName, BrandName }), "{}",
            command.UserId, command.UserName,
            $"{BrandName} / {fuelName}: removed",
            Brand!.Slug);

        // One save for the archive and the audit row: the row and the change it describes commit
        // together, or neither does (planning #533).
        await _context.SaveChangesAsync(ct);

        return new AdminDeleteFuelResult { Success = true };
    }
}
