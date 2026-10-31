using FuelFlow.Features.Contracts.GetProvider;
using FuelFlow.Features.Contracts.GetVersions;
using FuelFlow.Features.Contracts.PublishVersion;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    private readonly GetContractVersionsQueryHandler _getVersionsHandler;
    private readonly GetContractProviderQueryHandler _getProviderHandler;
    private readonly PublishContractVersionCommandHandler _publishHandler;
    private readonly ContractTextExtractor _textExtractor;

    public AdminContractVersionsController(
        GetContractVersionsQueryHandler getVersionsHandler,
        GetContractProviderQueryHandler getProviderHandler,
        PublishContractVersionCommandHandler publishHandler,
        ContractTextExtractor textExtractor)
    {
        _getVersionsHandler = getVersionsHandler;
        _getProviderHandler = getProviderHandler;
        _publishHandler = publishHandler;
        _textExtractor = textExtractor;
    }

    /// <summary>
    /// The versions of one provider's text, newest first.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.Staff)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVersions([FromQuery] Guid brandId, CancellationToken cancellationToken)
    {
        var versions = await _getVersionsHandler.HandleAsync(
            new GetContractVersionsQuery(brandId), cancellationToken);

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
            return ApiError.BadRequest("BrandId is required", ContractRefusalCodes.BrandRequired);

        if (string.IsNullOrWhiteSpace(request.Body))
            return ApiError.BadRequest(
                "The text is required - a contract with no text is not a document",
                ContractRefusalCodes.TextRequired);

        if (request.TermDays <= 0)
            return ApiError.BadRequest(
                "The term must be at least one day - a contract that expires the moment it is made is not a contract",
                ContractRefusalCodes.TermRequired);

        var brand = await _getProviderHandler.HandleAsync(
            new GetContractProviderQuery(request.BrandId), cancellationToken);

        if (brand is null)
            return ApiError.NotFound("Provider not found", ContractRefusalCodes.ProviderNotFound);

        var published = await _publishHandler.HandleAsync(
            new PublishContractVersionCommand(request.BrandId, request.Body, request.TermDays), cancellationToken);

        return Ok(new
        {
            published.Id,
            published.BrandId,
            published.Version,
            published.TermDays,
            published.CreatedAtUtc,
            // How many companies were given a contract for this provider by this publish.
            contractsCreated = published.ContractsCreated
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
    // The multipart reader's ceiling is global, so a route that accepts a file this size raises
    // that one too - otherwise the reader refuses the body the route accepts.
    [RequestFormLimits(MultipartBodyLengthLimit = 20 * 1024 * 1024)]
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
            return ApiError.BadRequest("BrandId is required", ContractRefusalCodes.BrandRequired);

        if (termDays <= 0)
            return ApiError.BadRequest(
                "The term must be at least one day - a contract that expires the moment it is made is not a contract",
                ContractRefusalCodes.TermRequired);

        if (file is null || file.Length == 0)
            return ApiError.BadRequest(
                "A document is required - the contract text is a Word or PDF file",
                ContractRefusalCodes.DocumentRequired);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!ContractTextExtractor.SupportedExtensions.Contains(extension))
            return ApiError.BadRequest(
                $"Only {string.Join(" and ", ContractTextExtractor.SupportedExtensions)} documents are read",
                ContractRefusalCodes.DocumentUnsupported);

        // The provider is looked up before the document is read: an upload for a provider that is
        // not there is a mistake in the form, and reading a document for it would be work for
        // nothing.
        var brand = await _getProviderHandler.HandleAsync(
            new GetContractProviderQuery(brandId), cancellationToken);

        if (brand is null)
            return ApiError.NotFound("Provider not found", ContractRefusalCodes.ProviderNotFound);

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
            return ApiError.BadRequest(
                $"The document is too large to read: it expands to {ex.SizeBytes} bytes, and this "
                + $"reader opens up to {ContractTextExtractor.MaxDocumentBytes}. If it is a real "
                + "document, ask the provider for a shorter one.",
                ContractRefusalCodes.DocumentTooLarge,
                new Dictionary<string, object?>
                {
                    ["sizeBytes"] = ex.SizeBytes,
                    ["limitBytes"] = ContractTextExtractor.MaxDocumentBytes
                });
        }

        if (string.IsNullOrWhiteSpace(body))
            return ApiError.BadRequest(
                "The document has no text in it. A scanned document is a picture of a contract, "
                + "and a contract the customer cannot read is not a contract.",
                ContractRefusalCodes.DocumentNoText);

        var published = await _publishHandler.HandleAsync(
            new PublishContractVersionCommand(brandId, body, termDays), cancellationToken);

        return Ok(new
        {
            published.Id,
            published.BrandId,
            published.Version,
            published.TermDays,
            published.CreatedAtUtc,
            // How much text was read out of the document, so the operator can see that the
            // upload was read and not stored as an empty page.
            characters = published.Characters,
            // How many companies were given a contract for this provider by this publish.
            contractsCreated = published.ContractsCreated
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
