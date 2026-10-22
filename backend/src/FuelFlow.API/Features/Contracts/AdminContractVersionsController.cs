using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
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
public sealed class AdminContractVersionsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ContractTextExtractor _textExtractor;
    private readonly ContractWriter _contractWriter;

    public AdminContractVersionsController(
        ApplicationDbContext dbContext,
        ContractTextExtractor textExtractor,
        ContractWriter contractWriter)
    {
        _dbContext = dbContext;
        _textExtractor = textExtractor;
        _contractWriter = contractWriter;
    }

    /// <summary>
    /// The versions of one provider's text, newest first.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.Staff)]
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
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
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
            CreatedAtUtc = SystemClock.UtcNow,
            CreatedByUserId = null
        };

        _dbContext.ContractVersions.Add(version);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // The backfill: a company that was created before this provider published any text has
        // no contract for it, and without this it never would - the writer only runs when a
        // company is created. This is the owner's answer to "do the companies that already
        // existed get contracts too?".
        var contractsCreated = await _contractWriter.EnsureContractsForAllAsync(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            version.Id,
            version.BrandId,
            version.Version,
            version.TermDays,
            version.CreatedAtUtc,
            // How many companies were given a contract for this provider by this publish.
            contractsCreated
        });
    }

    /// <summary>
    /// Publishes a new version of a provider's text from a document the operator uploaded.
    /// </summary>
    /// <remarks>
    /// <b>The text comes from a file, not from a text box.</b> The owner decided on 2026-10-21
    /// that the contract text is a ready-made Word or PDF document the operator hands over, and
    /// the platform reads it. The document is read for its text, and the text is what the
    /// customer reviews before signing and what the version points at.
    /// <para>
    /// A document with no text in it is refused rather than stored: a scanned PDF is a picture
    /// of a document, and a contract whose text is empty is a document nobody can review.
    /// </para>
    /// </remarks>
    [HttpPost("upload")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Upload(
        [FromForm] Guid brandId,
        [FromForm] int termDays,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (brandId == Guid.Empty)
            return BadRequest("BrandId is required");

        if (termDays <= 0)
            return BadRequest("The term must be at least one day - a contract that expires the moment it is made is not a contract");

        if (file is null || file.Length == 0)
            return BadRequest("A document is required - the contract text is a Word or PDF file");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!ContractTextExtractor.SupportedExtensions.Contains(extension))
            return BadRequest($"Only {string.Join(" and ", ContractTextExtractor.SupportedExtensions)} documents are read");

        var brand = await _dbContext.Brands
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == brandId, cancellationToken);

        if (brand is null)
            return NotFound("Provider not found");

        string? body;
        try
        {
            await using (var content = file.OpenReadStream())
            {
                body = await _textExtractor.ExtractAsync(content, file.FileName, cancellationToken);
            }
        }
        catch (ContractDocumentTooLargeException ex)
        {
            // A small upload that expands into gigabytes: the read is refused rather than
            // allowed to ask the process for the memory.
            return BadRequest(
                $"The document is too large to read: it expands to {ex.SizeBytes} bytes, and this "
                + $"reader opens up to {ContractTextExtractor.MaxDocumentBytes}. If it is a real "
                + "document, ask the provider for a shorter one.");
        }

        if (string.IsNullOrWhiteSpace(body))
            return BadRequest(
                "The document has no text in it. A scanned document is a picture of a contract, "
                + "and a contract the customer cannot read is not a contract.");

        var current = await _dbContext.ContractVersions
            .Where(v => v.BrandId == brandId)
            .OrderByDescending(v => v.Version)
            .Select(v => (short?)v.Version)
            .FirstOrDefaultAsync(cancellationToken);

        var version = new ContractVersion
        {
            Id = Guid.CreateVersion7(),
            BrandId = brandId,
            Version = (short)((current ?? 0) + 1),
            Body = body.Trim(),
            TermDays = termDays,
            CreatedAtUtc = SystemClock.UtcNow,
            CreatedByUserId = null
        };

        _dbContext.ContractVersions.Add(version);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // The backfill, for the same reason as in Publish: a company created before this
        // provider published any text has no contract for it, and only the writer can give
        // it one.
        var contractsCreated = await _contractWriter.EnsureContractsForAllAsync(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            version.Id,
            version.BrandId,
            version.Version,
            version.TermDays,
            version.CreatedAtUtc,
            // How much text was read out of the document, so the operator can see that the
            // upload was read and not stored as an empty page.
            characters = version.Body.Length,
            // How many companies were given a contract for this provider by this publish.
            contractsCreated
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
