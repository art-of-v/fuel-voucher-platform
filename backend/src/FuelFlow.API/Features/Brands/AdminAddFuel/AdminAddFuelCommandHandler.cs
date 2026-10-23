using System.Text.Json;
using FuelFlow.Features.Pricing;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.AdminAddFuel;

/// <summary>
/// Puts a fuel into a network's catalogue, with its sizes, and records the price it was born with.
/// </summary>
/// <remarks>
/// The refusals are the interesting half. A grade that is already there - spelled the same way or
/// spelled another way - and a code that another fuel already holds are all refused here rather
/// than left to the unique indexes, because an index says "23505" and names neither the fuel that
/// won nor the one that lost.
/// </remarks>
public sealed class AdminAddFuelCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;

    // Every price this handler writes goes through here, so the history cannot be missed by the one
    // endpoint that forgets. See PriceHistoryRecorder for why that is a service rather than a comment.
    private readonly PriceHistoryRecorder _priceRecorder;

    public AdminAddFuelCommandHandler(
        ApplicationDbContext context,
        AuditEventService eventService,
        PriceHistoryRecorder priceRecorder)
    {
        _context = context;
        _eventService = eventService;
        _priceRecorder = priceRecorder;
    }

    public async Task<AdminAddFuelResult> HandleAsync(AdminAddFuelCommand command, CancellationToken ct = default)
    {
        var request = command.Fuel;

        // Required for the same reason as on UpdateFuel: every package created below inherits it,
        // so an unstated flag would default to "sell this".
        if (!request.IsTestData.HasValue)
            return new AdminAddFuelResult
            {
                Error = "isTestData is required. Set it to true for rehearsal data, or false if this "
                          + "is a real product.",
                Field = nameof(request.IsTestData)
            };

        var Brand = await _context.Brands.FirstOrDefaultAsync(s => s.Id == command.BrandId, ct);
        if (Brand is null) return new AdminAddFuelResult { NotFound = true };

        // One fuel type per grade per Brand, refused rather than accepted as a second row. This fuel
        // id is generated here, so nothing about the write collides on a duplicate the way it did when
        // the id was the brand-encoded 'okko-95' - and a twin is not a cosmetic duplicate: it becomes a
        // second row in the catalogue, the storefront lists one fuel at two prices, and the voucher importer resolves
        // the fuel by name with FirstOrDefault (OkkoFuelClassifier/WogFuelClassifier), so the twin
        // without the imported pump price is the one a real voucher can land on. The database refuses
        // the same thing with ux_fuel_types_brand_name; this says which fuel already owns the name.
        //
        // What the brand already carries is read once, by name, because there are two questions to
        // ask of it: the exact duplicate ("А-95" is already there) and the near-duplicate ("A-95" is
        // the same grade as "А-95"). The second is the one an index on the name cannot answer, and
        // it is answered here by the grade key - the same rule the database's
        // ux_fuel_types_brand_grade_key enforces, so the operator is told which row won instead of
        // meeting a 23505 that names neither fuel.
        var carried = await _context.FuelTypes
            .Where(f => f.BrandId == command.BrandId)
            .Select(f => new { f.Id, f.Name })
            .ToListAsync(ct);

        var duplicate = carried.FirstOrDefault(f => f.Name == request.Name);
        if (duplicate is not null)
        {
            return new AdminAddFuelResult
            {
                Conflict = true,
                Error = $"'{request.Name}' already exists at {Brand.Name}. Price it with PUT "
                          + $"fuels/{duplicate.Id} instead of adding it twice: a second row for the same "
                          + "fuel is a second row in the catalogue, and a voucher can then be priced against whichever "
                          + "of the two the importer happens to pick.",
                FuelTypeId = duplicate.Id,
                Field = nameof(request.Name)
            };
        }

        // The same grade, spelled another way: "А-95" and "A-95" are one fuel, and the operator is
        // told which of the two the catalogue already has.
        var gradeKey = FuelGrade.Key(request.Name);
        if (gradeKey is not null)
        {
            var twin = carried.FirstOrDefault(f => FuelGrade.Key(f.Name) == gradeKey);
            if (twin is not null)
            {
                return new AdminAddFuelResult
                {
                    Conflict = true,
                    Error = $"'{request.Name}' is the same grade as '{twin.Name}', which already exists at "
                              + $"{Brand.Name}. Price it with PUT fuels/{twin.Id} instead of adding it twice: "
                              + "one grade is one row, and a second row for it is a second row in the catalogue, "
                              + "which the storefront lists at a second price and a voucher can be priced against.",
                    FuelTypeId = twin.Id,
                    Field = nameof(request.Name)
                };
            }
        }

        // The same question asked of the code rather than the name, and refused in the same shape.
        // A code is the half of a fuel's identity that is not free text, so two fuels claiming one at
        // the same network is the ambiguity the catalogue import would resolve arbitrarily - and the
        // index below would answer 23505 naming neither fuel.
        var brandFuelCode = FuelTypeEntity.NormaliseBrandFuelCode(request.BrandFuelCode);
        if (brandFuelCode is not null)
        {
            var codeOwner = await _context.FuelTypes
                .Where(f => f.BrandId == command.BrandId && f.BrandFuelCode == brandFuelCode)
                .Select(f => f.Id)
                .FirstOrDefaultAsync(ct);

            if (codeOwner != Guid.Empty)
            {
                return new AdminAddFuelResult
                {
                    Conflict = true,
                    Error = $"Brand fuel code '{brandFuelCode}' is already taken at {Brand.Name} by fuel "
                              + $"{codeOwner}. A code identifies one fuel per network: two fuels claiming "
                              + "it is what makes a catalogue import pick the wrong row.",
                    FuelTypeId = codeOwner,
                    Field = nameof(request.BrandFuelCode)
                };
            }
        }

        if (!request.IsTestData.Value)
        {
            var token = RehearsalMarkers.MatchedToken(null, request.Name);
            if (token is not null)
            {
                return new AdminAddFuelResult
                {
                    Error = $"The name contains \"{token}\", which reads as rehearsal data, but "
                              + "isTestData is false. Set it to true, or rename the fuel if this is "
                              + "a real product.",
                    Field = nameof(request.IsTestData)
                };
            }
        }

        // The numbers themselves, refused here rather than by the database. The checks the database
        // makes are real (ck_fuel_types_margin_signs, ck_fuel_types_allow_below_cost_needs_a_cost),
        // but a check constraint answers "23514" and names neither the knob nor the operator, and a
        // zero cost is one the constraints let through and the pricing must not: a litre we paid
        // nothing for has no cost to guard a sale against.
        if (FuelEconomics.Fault(request.SupplierPricePerLiter, request.MarginUahPerLiter, request.AllowBelowCost)
            is { } fault)
        {
            return new AdminAddFuelResult { Error = fault.Error, Field = fault.Field };
        }

        // The price, or null when we cannot know it. A cost that is not stated is not a zero: the
        // model refuses to build a price out of a missing number, because "we do not know what we
        // paid" is not the same fact as "we paid nothing".
        var finalPerLiter = FuelPricing.FinalPerLiterOrNull(
            request.SupplierPricePerLiter, request.MarginUahPerLiter);

        // The price the customer pays is COMPUTED from the knobs, so there is nothing to store for it
        // and nothing to write. The column price is the reference the saving is measured against,
        // and it is a knob of its own - the operator's or the importer's.
        //
        // With no stated cost there is no price at all: the fuel is unpriced, the storefront shows
        // it at the column with "not in stock", and it cannot be sold until the cost is recorded.
        var fuel = new FuelTypeEntity
        {
            Id = Guid.CreateVersion7(),
            BrandId = command.BrandId,
            Name = request.Name,
            BrandFuelCode = brandFuelCode,
            // The margin is born on the fuel. It used to be written only to the offer row, because that
            // row was where every reader found it; the offers are gone (2026-10-19) and the fuel is where
            // every reader looks, and a fuel created without a margin prices at cost - silently, and
            // correctly by the formula.
            MarginUahPerLiter = request.MarginUahPerLiter,
            AllowBelowCost = request.AllowBelowCost,
            CreatedAtUtc = SystemClock.UtcNow,
            UpdatedAtUtc = SystemClock.UtcNow
        };
        _context.FuelTypes.Add(fuel);
        await _context.SaveChangesAsync(ct);

        var litersToCreate = request.PackageLiters;
        if (litersToCreate.Count == 0)
        {
            var existingFuelIds = await _context.FuelTypes
                .Where(f => f.BrandId == command.BrandId && f.Id != fuel.Id)
                .Select(f => f.Id).ToListAsync(ct);
            litersToCreate = await _context.FuelNominals
                .Where(n => existingFuelIds.Contains(n.FuelTypeId))
                .Select(n => (int)n.Liters)
                .Distinct()
                .OrderBy(l => l)
                .ToListAsync(ct);
        }

        if (litersToCreate.Count == 0)
        {
            litersToCreate = DefaultNominalVolumes.Liters;
        }

        // Every per-litre knob the request carries is a property of WHAT IS BEING SOLD, not of how much of
        // it: a 5 L bottle and a 50 L canister of the same fuel have one price per litre. They used to
        // be written on a per-pool offer row, because that row could hold two prices for one grade; with
        // the pool on the size list they are written on the fuel and there is no second place to keep
        // them in step.
        var pool = DataKinds.ForPool(request.IsTestData.Value);
        // The cost and the margin are nullable on the request, and null is written as null: a cost
        // that is not stated is not a zero. The fuel is allowed to be born unpriced - it has no
        // stock behind it yet - and what it may not do is carry a price built out of a number
        // nobody entered. Only the column price and the margin percent are "not stated" for their
        // own reasons, and those stay null rather than being invented as zero either.
        fuel.SupplierPricePerLiter = request.SupplierPricePerLiter;
        if (request.MarginPercent is not null) fuel.MarginPercent = request.MarginPercent;
        if (request.PumpPricePerLiter is not null) fuel.PumpPricePerLiter = request.PumpPricePerLiter;

        // A fuel that has never been priced has no previous price, so this row is not a change but the
        // price it was born with - the same as every later row's role, and the reason a fuel's history
        // starts at its creation rather than at the first time somebody edited it.
        await _priceRecorder.RecordAsync(fuel, PriceHistoryReasons.Operator, command.UserId, ct: ct);

        foreach (var liters in litersToCreate)
        {
            // The sizes are the fuel's, stated once. No price override either: a fuel added through
            // this screen is priced like itself, and the "before" price the storefront strikes
            // through is recomputed from the pump price via FuelPricing.OriginalPackagePrice
            // (planning #73), so the supplier cost never leaks to anonymous callers.
            _context.FuelNominals.Add(new FuelNominal
            {
                FuelTypeId = fuel.Id,
                // In the pool the request named. The pool is part of a size's identity and it is
                // what the storefront filters on, so a size written in the wrong pool is a size in the
                // wrong catalogue - which is how a rehearsal grade added here would be
                // published on the public shelf (planning #460).
                DataKind = pool,
                Liters = liters,
                CreatedAtUtc = SystemClock.UtcNow,
                UpdatedAtUtc = SystemClock.UtcNow,
            });
        }

        var newValue = JsonSerializer.Serialize(new { fuel.Id, fuel.Name, request.SupplierPricePerLiter, request.MarginUahPerLiter, request.PumpPricePerLiter, FinalPricePerLiter = finalPerLiter });
        _eventService.RecordEvent(
            "Fuel", fuel.Id.ToString(), "FuelAdded",
            null, newValue, command.UserId, command.UserName,
            $"{Brand.Name} / {fuel.Name}: added at {(finalPerLiter ?? request.PumpPricePerLiter ?? 0m):F2} UAH/L",
            Brand!.Slug);

        // One save for the sizes, the price history and the audit row: the row and the change it
        // describes commit together, or neither does (planning #533).
        await _context.SaveChangesAsync(ct);

        return new AdminAddFuelResult { Success = true, CreatedFuelId = fuel.Id };
    }
}
