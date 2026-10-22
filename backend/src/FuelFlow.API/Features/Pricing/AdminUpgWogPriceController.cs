using System.Security.Claims;
using System.Text;
using FuelFlow.Features.Pricing.ImportUpgWogPrices;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Pricing;

/// <summary>
/// Uploads a price sheet produced by <c>scripts/fetch-upg-wog-prices.mjs</c> for one brand.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>AdminOkkoPumpPriceController</c>, and shaped like it on purpose: the CSV
/// contract is shared (<c>fuelCode,pricePerLiter</c>), the body arrives as a multipart file, and
/// <c>dryRun</c> defaults to true so the first upload of any sheet is a rehearsal.
/// </para>
/// <para>
/// The fetcher runs on a workstation rather than on this box - UPG and WOG publish the price blob
/// inside their HTML rather than through an API, which is why they have a scraper and OKKO does not.
/// That is a reason for the file, not for the gap: this endpoint is what makes the scraper's output
/// reach the catalogue, so the brands that were hand-typed stop being hand-typed.
/// </para>
/// </remarks>
[ApiController]
[Route("api/admin/brand-prices")]
[Authorize(Policy = AuthPolicies.CatalogueWrite)]
public sealed class AdminUpgWogPriceController : ControllerBase
{
    private readonly ImportUpgWogPricesCommandHandler _import;

    public AdminUpgWogPriceController(ImportUpgWogPricesCommandHandler import) => _import = import;

    /// <summary>The brands this endpoint accepts. Anything else is a copy-paste into the wrong form.</summary>
    public static readonly string[] SupportedBrands = ImportUpgWogPricesCommandHandler.SupportedBrandIds;

    [HttpPost("{brand}/import")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Import(
        string brand,
        IFormFile file,
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default)
    {
        if (!SupportedBrands.Contains(brand.Trim().ToLowerInvariant()))
            return ApiError.BadRequest($"Unsupported brand '{brand}'. Supported: {string.Join(", ", SupportedBrands)}");

        if (file is null || file.Length == 0)
            return ApiError.BadRequest("A non-empty file is required");

        string content;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
            content = await reader.ReadToEndAsync(ct);

        var result = await _import.HandleAsync(
            new ImportUpgWogPricesCommand(content, brand, dryRun),
            GetUserId(),
            GetUserName(),
            ct);

        return Ok(result);
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private string? GetUserName()
    {
        var first = User.FindFirst("first_name")?.Value;
        var last = User.FindFirst("last_name")?.Value;
        if (first is not null && last is not null) return $"{first} {last}";
        var name = User.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}