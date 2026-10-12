using FuelFlow.Features.Stations.GetPublicPackages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Stations;

[ApiController]
// Explicit opt-out of the global FallbackPolicy (AuthSetup): the store front has to be browsable
// before sign-in. Both actions project PublicPackageResponse (store-front fields only) — never the
// raw FuelPackage entity, which carries the operator's supplier cost + margin (planning #52).
[AllowAnonymous]
// No ResponseCache here: package prices change from the admin panel and a cached
// response would show users a price different from what checkout actually charges.
[Route("api/packages")]
public sealed class PackageController : ControllerBase
{
    private readonly GetPublicPackagesQueryHandler _getAll;

    public PackageController(GetPublicPackagesQueryHandler getAll)
    {
        _getAll = getAll;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetPublicPackagesQuery(), ct));
}
