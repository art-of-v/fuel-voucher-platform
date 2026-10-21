using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts;

/// <summary>
/// The provider's contract text: the one text per provider, and the versions of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The contracts table had no writer and no text: nothing created a
/// contract, and the table had nowhere to keep a document, so the customer's "REVIEW THE
/// TEXT" screen had nothing behind it. The owner decided on 2026-10-21 that the text lives
/// once per provider and is versioned, and that a new version changes the text for every
/// contract of that provider while the old version is kept - what a customer signed is a
/// question the audit trail has to answer.
/// </para>
/// <para>
/// <b>Why a new version and not an edit.</b> A contract says which version it is against, and
/// a signature records the version it was signed against. Overwriting the text would make
/// both of those numbers lie, and the number is the only thing that ties a signature to the
/// words the customer actually read.
/// </para>
/// </remarks>
[ApiController]
[Route("api/admin/contract-versions")]
[Authorize(Policy = "Staff")]
public sealed class AdminContractVersionsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminContractVersionsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// The versions of one provider's text, newest first.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVersions([FromQuery] Guid brandId, CancellationToken cancellationToken)
    {
        var versions = await _dbContext.ContractVersions
            .AsNoTracking()
            .Where(v => v.BrandId == brandId)
            .OrderByDescending(v => v.Version)
            .Select(v => new
            {
                v.Id,
                v.BrandId,
                v.Version,
                v.Body,
                v.TermDays,
                v.CreatedAtUtc,
                v.CreatedByUserId
            })
            .ToListAsync(cancellationToken);

        return Ok(versions);
    }

    /// <summary>
    /// Publishes a new version of a provider's text.
    /// </summary>
    /// <remarks>
    /// The version number is the next one for that provider, and the database's unique index
    /// on (brand, version) is what makes two concurrent publishes of the same number fail
    /// rather than quietly produce two texts with the same name.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Publish([FromBody] PublishContractVersionRequest request, CancellationToken cancellationToken)
    {
        if (request.BrandId == Guid.Empty)
            return BadRequest("BrandId is required");

        if (string.IsNullOrWhiteSpace(request.Body))
            return BadRequest("The text is required - a contract with no text is not a document");

        if (request.TermDays <= 0)
            return BadRequest("The term must be at least one day - a contract that expires the moment it is made is not a contract");

        var brand = await _dbContext.Brands
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == request.BrandId, cancellationToken);

        if (brand is null)
            return NotFound("Provider not found");

        var current = await _dbContext.ContractVersions
            .Where(v => v.BrandId == request.BrandId)
            .OrderByDescending(v => v.Version)
            .Select(v => (short?)v.Version)
            .FirstOrDefaultAsync(cancellationToken);

        var version = new ContractVersion
        {
            Id = Guid.CreateVersion7(),
            BrandId = request.BrandId,
            Version = (short)((current ?? 0) + 1),
            Body = request.Body.Trim(),
            TermDays = request.TermDays,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = null
        };

        _dbContext.ContractVersions.Add(version);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            version.Id,
            version.BrandId,
            version.Version,
            version.TermDays,
            version.CreatedAtUtc
        });
    }

    /// <summary>What a new version of a provider's text is made of.</summary>
    public sealed class PublishContractVersionRequest
    {
        /// <summary>The provider whose text this is.</summary>
        public Guid BrandId { get; set; }

        /// <summary>The text the customer reads before signing.</summary>
        public string? Body { get; set; }

        /// <summary>How long a contract made against this version is valid, in days.</summary>
        public int TermDays { get; set; }
    }
}
