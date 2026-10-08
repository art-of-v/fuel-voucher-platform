using System.Security.Claims;
using System.Text;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Pricing;

/// <summary>
/// Bulk-imports OKKO's published pump prices (колонка) into the OKKO catalog.
/// <para>
/// Separate from <c>ProvidersController</c> because the write is brand-scoped and machine-fed: the
/// body is the CSV emitted by <c>scripts/fetch-okko-prices.mjs</c>, not a typed-out
/// <c>ProviderFuelDto</c>. Posting with <c>dryRun=true</c> returns the preview without touching the
/// catalog, so the operator can confirm the scraper read the site correctly before committing.
/// </para>
/// <para>
/// Staff policy, matching the manual fuel write path it stands in for.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/providers/okko/pump-prices")]
[Authorize(Policy = "Staff")]
public sealed class AdminOkkoPumpPriceController : ControllerBase
{
    private readonly ImportOkkoPumpPricesCommandHandler _import;

    public AdminOkkoPumpPriceController(ImportOkkoPumpPricesCommandHandler import) => _import = import;

    [HttpPost("import")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Import(
        IFormFile file,
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("A non-empty file is required");

        string content;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
            content = await reader.ReadToEndAsync(ct);

        var result = await _import.HandleAsync(
            new ImportOkkoPumpPricesCommand(content, dryRun),
            GetUserId(), GetUserName(), ct);

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
        if (first is not null) return first;
        if (last is not null) return last;
        return User.FindFirst(ClaimTypes.Name)?.Value;
    }
}