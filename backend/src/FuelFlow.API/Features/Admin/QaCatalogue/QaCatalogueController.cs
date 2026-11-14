using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Admin.QaCatalogue;

/// <summary>
/// Stocks the rehearsal catalogue the App Store reviewer shops from.
/// </summary>
/// <remarks>
/// Behind the same capability as the QA test-access switch (Admin/ProductOwner): both decide what a
/// QA session can reach, and neither is a routine operator action. Every voucher this creates carries
/// <c>data_kind = 'qa'</c>, so it is invisible to real customers by the same rule that hides it from
/// the marketing site - the storefront, checkout and fulfilment all resolve the caller's pool before
/// they match anything.
/// </remarks>
[ApiController]
[Route("api/admin/qa-catalogue")]
public sealed class QaCatalogueController : ControllerBase
{
    private readonly SeedQaCatalogueCommandHandler _seed;

    public QaCatalogueController(SeedQaCatalogueCommandHandler seed) => _seed = seed;

    [HttpPost("seed")]
    [Authorize(Policy = AuthPolicies.QaTestAccess)]
    [ProducesResponseType(typeof(SeedQaCatalogueResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Seed(
        [FromBody] SeedQaCatalogueRequest? request,
        CancellationToken cancellationToken) =>
        Ok(await _seed.HandleAsync(
            new SeedQaCatalogueCommand(request?.VouchersPerPackage),
            cancellationToken));
}

/// <summary>How deep to stock each package. Omitted means the handler's default.</summary>
public sealed record SeedQaCatalogueRequest(int? VouchersPerPackage);
