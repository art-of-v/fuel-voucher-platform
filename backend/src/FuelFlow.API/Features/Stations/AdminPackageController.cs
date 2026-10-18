using System.Security.Claims;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Brands.CreatePackage;
using FuelFlow.Features.Brands.DeletePackage;
using FuelFlow.Features.Brands.GetAdminPackages;
using FuelFlow.Features.Brands.GetAdminPackagesByBrand;
using FuelFlow.Features.Brands.GetPackageSuggestions;
using FuelFlow.Features.Brands.UpdatePackage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Brands;

[ApiController]
[Route("api/admin/packages")]
[Authorize(Policy = "Staff")]
public sealed class AdminPackageController : ControllerBase
{
    private readonly GetAdminPackagesQueryHandler _getAll;
    private readonly GetAdminPackagesByBrandQueryHandler _getByBrand;
    private readonly GetPackageSuggestionsQueryHandler _getSuggestions;
    private readonly CreatePackageCommandHandler _create;
    private readonly UpdatePackageCommandHandler _update;
    private readonly DeletePackageCommandHandler _delete;

    public AdminPackageController(
        GetAdminPackagesQueryHandler getAll,
        GetAdminPackagesByBrandQueryHandler getByBrand,
        GetPackageSuggestionsQueryHandler getSuggestions,
        CreatePackageCommandHandler create,
        UpdatePackageCommandHandler update,
        DeletePackageCommandHandler delete)
    {
        _getAll = getAll;
        _getByBrand = getByBrand;
        _getSuggestions = getSuggestions;
        _create = create;
        _update = update;
        _delete = delete;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetAdminPackagesQuery(), ct));

    // Removed 2026-10-12 (planning #289): no caller in any client, script, deploy config, doc or
    // test. They were previously "disabled" by commenting out the [HttpGet] attribute, which does
    // not disable an action - under [ApiController] a method with no HTTP attribute binds to the
    // controller prefix, so four actions here plus GetAll all claimed `api/admin/packages` and the
    // router answered 500 (AmbiguousMatchException) instead of 404. Deleting the method is the way
    // to remove an endpoint; commenting the attribute out is not.

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePackageRequest request, CancellationToken ct)
    {
        // Nullable so that "omitted" is distinguishable from "false". It used to bind the raw entity,
        // where a missing isTestData deserialised to false - which means "sell this" - so a package
        // created for a rehearsal with the field forgotten went live. That is how
        // "А95 ЄВРО (ТЕСТ 1ГРН)" reached the public price radar at 1 UAH/liter.
        if (!request.IsTestData.HasValue)
        {
            return BadRequest(new
            {
                message = "isTestData is required. Set it to true for rehearsal data, or false if this "
                          + "is a real product. It defaults to nothing rather than guessing, because "
                          + "guessing wrong publishes rehearsal stock to the storefront.",
                field = nameof(request.IsTestData)
            });
        }

        // The flag is now always stated, so the second half is to make a stated "false" believable:
        // a name that reads as rehearsal cannot be published as live. Not a substitute for the flag -
        // an operator naming a rehearsal fuel innocently ("Test-A95" as a grade name) can still set
        // the flag - but it stops the silent case, which is the one that reached customers.
        if (!request.IsTestData.Value)
        {
            var token = RehearsalMarkers.MatchedToken(request.Id, request.FuelName);
            if (token is not null)
            {
                return BadRequest(new
                {
                    message = $"The name contains \"{token}\", which reads as rehearsal data, but "
                              + "isTestData is false. Set isTestData to true, or rename it if this is "
                              + "a real product.",
                    field = nameof(request.IsTestData)
                });
            }
        }

        var result = await _create.HandleAsync(
            new CreatePackageCommand(request.ToPackage()),
            ct,
            ActingUserId());
        if (result.Error != null && !result.Conflict)
            return BadRequest(result.Error);
        if (result.Conflict)
            return Conflict(result.Error);
        return CreatedAtAction(nameof(GetAll), new { id = result.Package!.Id }, result.Package);
    }

    /// <summary>
    /// The staff member behind the call, for the price history this endpoint's handler appends. Null
    /// when the token carries no identifier - the row is still written, just without an actor.
    /// </summary>
    private Guid? ActingUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out var id) ? id : null;
    }
}

/// <summary>
/// The package fields a caller may set, with the rehearsal flag made explicit.
/// </summary>
/// <remarks>
/// Exists because the endpoint used to bind <see cref="FuelPackage"/> itself, which handed every
/// column - including the ones that must never be caller-chosen - to the request body. Only the
/// fields an operator actually fills in are writable here now.
/// </remarks>
public sealed record CreatePackageRequest
{
    public string Id { get; init; } = null!;
    public string BrandId { get; init; } = null!;
    public string FuelTypeId { get; init; } = null!;
    public string FuelName { get; init; } = null!;
    public decimal Liters { get; init; }

    /// <summary>
    /// This nominal's own price, or null/omitted to price it from the fuel's rate like every other
    /// size. It is an <b>override</b>, not a price: once set it survives every reprice of the fuel, so it
    /// is the exception rather than the way to price a catalogue. 
    /// <c>PUT /api/admin/packages/{id}</c> with null puts the nominal back on the fuel's rate.
    /// </summary>
    public decimal? PriceOverrideUah { get; init; }

    public decimal? SupplierPricePerLiter { get; init; }
    public decimal? MarginUahPerLiter { get; init; }
    public decimal? MarginPercent { get; init; }
    public decimal? FinalPricePerLiter { get; init; }
    public decimal? PumpPricePerLiter { get; init; }
    public decimal? MinDiscountPerLiter { get; init; }

    /// <summary>
    /// Required. Null means the caller did not say, which is answered with 400 rather than treated
    /// as <c>false</c>.
    /// </summary>
    public bool? IsTestData { get; init; }

    public FuelPackage ToPackage() => new()
    {
        Id = Id,
        BrandId = BrandId,
        FuelTypeId = FuelTypeId,
        FuelName = FuelName,
        Liters = Liters,
        // The effective price is computed, not taken from the body: FuelPricing.PackagePrice resolves
        // the override if there is one and the offer's rate otherwise. The handler reads
        // PriceOverrideUah, which is why the two are different numbers on purpose.
        PriceOverrideUah = PriceOverrideUah,
        SupplierPricePerLiter = SupplierPricePerLiter,
        MarginUahPerLiter = MarginUahPerLiter,
        MarginPercent = MarginPercent,
        FinalPricePerLiter = FinalPricePerLiter,
        PumpPricePerLiter = PumpPricePerLiter,
        MinDiscountPerLiter = MinDiscountPerLiter,
        IsTestData = IsTestData ?? false,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
}
