using System.Security.Claims;
using FuelFlow.Features.Contracts.CreateLegalEntity;
using FuelFlow.Features.Contracts.GetCustomerContracts;
using FuelFlow.Features.Contracts.GetCustomerSignedContracts;
using FuelFlow.Features.Contracts.GetLegalEntityProfile;
using FuelFlow.Features.Contracts.GetMyLegalEntities;
using FuelFlow.Features.Contracts.SignContract;
using FuelFlow.Features.Contracts.UpdateLegalEntity;
using FuelFlow.Features.Contracts.UpsertLegalEntityProfile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Contracts;

[ApiController]
[Route("api/legal-entity")]
[Authorize]
public sealed class LegalEntityController : ControllerBase
{
    private readonly ILogger<LegalEntityController> _logger;
    private readonly GetLegalEntityProfileQueryHandler _getLegalEntityProfileHandler;
    private readonly UpsertLegalEntityProfileCommandHandler _upsertLegalEntityProfileHandler;
    private readonly GetMyLegalEntitiesQueryHandler _getMyLegalEntitiesHandler;
    private readonly CreateLegalEntityCommandHandler _createLegalEntityHandler;
    private readonly UpdateLegalEntityCommandHandler _updateLegalEntityHandler;
    private readonly GetCustomerContractsQueryHandler _getCustomerContractsHandler;
    private readonly SignContractCommandHandler _signContractHandler;
    private readonly GetCustomerSignedContractsQueryHandler _getCustomerSignedContractsHandler;

    public LegalEntityController(
        ILogger<LegalEntityController> logger,
        GetLegalEntityProfileQueryHandler getLegalEntityProfileHandler,
        UpsertLegalEntityProfileCommandHandler upsertLegalEntityProfileHandler,
        GetMyLegalEntitiesQueryHandler getMyLegalEntitiesHandler,
        CreateLegalEntityCommandHandler createLegalEntityHandler,
        UpdateLegalEntityCommandHandler updateLegalEntityHandler,
        GetCustomerContractsQueryHandler getCustomerContractsHandler,
        SignContractCommandHandler signContractHandler,
        GetCustomerSignedContractsQueryHandler getCustomerSignedContractsHandler)
    {
        _logger = logger;
        _getLegalEntityProfileHandler = getLegalEntityProfileHandler;
        _upsertLegalEntityProfileHandler = upsertLegalEntityProfileHandler;
        _getMyLegalEntitiesHandler = getMyLegalEntitiesHandler;
        _createLegalEntityHandler = createLegalEntityHandler;
        _updateLegalEntityHandler = updateLegalEntityHandler;
        _getCustomerContractsHandler = getCustomerContractsHandler;
        _signContractHandler = signContractHandler;
        _getCustomerSignedContractsHandler = getCustomerSignedContractsHandler;
    }

    [HttpGet("profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var entity = await _getLegalEntityProfileHandler.HandleAsync(
            new GetLegalEntityProfileQuery(userId.Value),
            cancellationToken);

        return entity is null ? NotFound() : Ok(entity);
    }

    [HttpPost("profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpsertProfile([FromBody] UpsertLegalEntityRequest request, CancellationToken cancellationToken)
    {
        var result = await _upsertLegalEntityProfileHandler.HandleAsync(
            new UpsertLegalEntityProfileCommand(GetUserId(), DetailsOf(request)),
            cancellationToken);

        return result.Outcome switch
        {
            LegalEntityWriteOutcome.Invalid => BadRequest(result.Error!),
            LegalEntityWriteOutcome.Unauthenticated => Unauthorized(),
            LegalEntityWriteOutcome.Conflict => Conflict(result.Error!),
            LegalEntityWriteOutcome.NotFound => NotFound(result.Error!),
            _ => Ok(result.Entity)
        };
    }

    // Multi-company (epic #103): list all legal entities owned by the caller (0..N).
    // Additive alongside the legacy single-entity GET /profile; mobile migrates to this
    // in the nav-shell slice.
    [HttpGet("mine")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var entities = await _getMyLegalEntitiesHandler.HandleAsync(
            new GetMyLegalEntitiesQuery(userId.Value),
            cancellationToken);

        return Ok(entities);
    }

    // Multi-company (epic #103): create a NEW legal entity for the caller. Unlike the
    // legacy POST /profile (which upserts the caller's single entity by UserId), this
    // always inserts, so an owner can register multiple companies.
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] UpsertLegalEntityRequest request, CancellationToken cancellationToken)
    {
        var result = await _createLegalEntityHandler.HandleAsync(
            new CreateLegalEntityCommand(GetUserId(), DetailsOf(request)),
            cancellationToken);

        return result.Outcome switch
        {
            LegalEntityWriteOutcome.Invalid => BadRequest(result.Error!),
            LegalEntityWriteOutcome.Unauthenticated => Unauthorized(),
            LegalEntityWriteOutcome.Conflict => Conflict(result.Error!),
            LegalEntityWriteOutcome.NotFound => NotFound(result.Error!),
            _ => Ok(result.Entity)
        };
    }

    // Multi-company (epic #103 S3a): update a SPECIFIC owned legal entity by id. The legacy
    // POST /profile upserts the caller's single entity by UserId, so it can only ever edit the
    // owner's first company; this scopes the edit to the chosen entity so companies 2..N are
    // editable. Ownership is enforced — a foreign/unknown id is a 404.
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpsertLegalEntityRequest request, CancellationToken cancellationToken)
    {
        var result = await _updateLegalEntityHandler.HandleAsync(
            new UpdateLegalEntityCommand(GetUserId(), id, DetailsOf(request)),
            cancellationToken);

        return result.Outcome switch
        {
            LegalEntityWriteOutcome.Invalid => BadRequest(result.Error!),
            LegalEntityWriteOutcome.Unauthenticated => Unauthorized(),
            LegalEntityWriteOutcome.Conflict => Conflict(result.Error!),
            LegalEntityWriteOutcome.NotFound => NotFound(result.Error!),
            _ => Ok(result.Entity)
        };
    }
    // The route was lost on 2026-10-01: 934e3793 moved the method and dropped the attribute with
    // it, and the revert that put the body back (b52f1359, 2026-10-12) did not put the attribute
    // back. An action with no HTTP attribute does not disappear - it binds to the controller's
    // prefix and answers EVERY method at /api/legal-entity. So the mobile app's
    // GET /api/legal-entity/contracts was a 404, and the list of contracts to sign was empty for
    // every customer. The guard is EndpointMethodContractTests.
    [HttpGet("contracts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetContracts(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var contracts = await _getCustomerContractsHandler.HandleAsync(
            new GetCustomerContractsQuery(userId.Value),
            cancellationToken);

        return Ok(contracts);
    }

    /// <summary>
    /// Sign ONE contract. The body is <c>{ contractId, signatureData }</c> - the shape the mobile
    /// client posts, and the only one this endpoint binds.
    /// <para>
    /// One contract per call, deliberately: the screen signs one document at a time (the user
    /// picks a contract and signs it), so a batch body would be a shape no caller has. A client
    /// that posted <c>{ contractIds: [...], stationId }</c> bound nothing - <c>ContractId</c>
    /// stayed <c>Guid.Empty</c> and every call was a 400, which is what the mobile client did
    /// for as long as it existed.
    /// </para>
    /// </summary>
    [HttpPost("sign")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Sign([FromBody] SignContractRequest request, CancellationToken cancellationToken)
    {
        var result = await _signContractHandler.HandleAsync(
            new SignContractCommand(GetUserId(), request.ContractId, request.SignatureData),
            cancellationToken);

        return result.Outcome switch
        {
            SignContractOutcome.Invalid => BadRequest(result.Error!),
            SignContractOutcome.Unauthenticated => Unauthorized(),
            SignContractOutcome.NotFound => NotFound(result.Error!),
            SignContractOutcome.Conflict => Conflict(result.Error!),
            _ => Ok(result.Signature)
        };
    }

    [HttpGet("contracts/signed")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSignedContracts(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var signed = await _getCustomerSignedContractsHandler.HandleAsync(
            new GetCustomerSignedContractsQuery(userId.Value),
            cancellationToken);

        return Ok(signed);
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private static LegalEntityDetails DetailsOf(UpsertLegalEntityRequest request) => new(
        request.Name,
        request.Edrpou,
        request.VatNumber,
        request.Address,
        request.DirectorName,
        request.Phone,
        request.Email);
}

public sealed class UpsertLegalEntityRequest
{
    public string Name { get; set; } = null!;
    public string Edrpou { get; set; } = null!;
    public string? VatNumber { get; set; }
    public string? Address { get; set; }
    public string? DirectorName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
}

public sealed class SignContractRequest
{
    public Guid ContractId { get; set; }
    public string? SignatureData { get; set; }
}
