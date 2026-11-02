using FuelFlow.Features.Brands.GetPublicPackages;
using FuelFlow.Features.Brands.GetPublicPackagesByBrand;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Brands;

[ApiController]
// Explicit opt-out of the global FallbackPolicy (AuthSetup): the store front has to be browsable
// before sign-in. Both actions project PublicPackageResponse (store-front fields only) — never the
// raw FuelPackage entity, which carries the operator's supplier cost + margin (planning #52).
[AllowAnonymous]
// No ResponseCache here: package prices change from the admin panel and a cached
// response would show users a price different from what checkout actually charges.
//
// [AllowAnonymous] relaxes AUTHORIZATION, not AUTHENTICATION: a bearer token that arrives is still
// validated and still populates User. The answer no longer depends on the caller — every caller,
// including the QA account, gets the one real catalogue — so a cache would be safe here. It is still
// not added, because prices change from the admin panel and a stale price is exactly what this
// controller must never serve.
[Route("api/packages")]
public sealed class PackageController : ControllerBase
{
    private readonly GetPublicPackagesQueryHandler _getAll;
    private readonly GetPublicPackagesByBrandQueryHandler _getByBrand;

    public PackageController(
        GetPublicPackagesQueryHandler getAll,
        GetPublicPackagesByBrandQueryHandler getByBrand)
    {
        _getAll = getAll;
        _getByBrand = getByBrand;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetPublicPackagesQuery(), ct));

    [HttpGet("{brandId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBrand([FromRoute] Guid BrandId, CancellationToken ct) =>
        Ok(await _getByBrand.HandleAsync(new GetPublicPackagesByBrandQuery(BrandId), ct));
}