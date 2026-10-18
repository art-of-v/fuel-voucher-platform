using System.Security.Claims;
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
// validated and still populates User, which is what lets IsAvailable reflect the caller's own stock
// pool. A signed-in QA account is only ever served test vouchers, so a cached-or-anonymous answer would
// advertise stock the shop then refuses to sell them.
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
        Ok(await _getAll.HandleAsync(new GetPublicPackagesQuery(CallerUserId()), ct));

    [HttpGet("{brandId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBrand([FromRoute] string BrandId, CancellationToken ct) =>
        Ok(await _getByBrand.HandleAsync(new GetPublicPackagesByBrandQuery(BrandId, CallerUserId()), ct));

    /// <summary>
    /// The caller, or null when nobody is signed in. Same claim lookup the purchase controller uses, and
    /// deliberately forgiving: an unparseable claim is treated as anonymous rather than as an error,
    /// because this endpoint is browsable and must stay that way.
    /// </summary>
    private Guid? CallerUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }
}