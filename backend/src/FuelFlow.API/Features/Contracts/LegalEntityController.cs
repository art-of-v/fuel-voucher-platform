using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Security.Claims;

namespace FuelFlow.Features.Contracts;

[ApiController]
[Route("api/legal-entity")]
[Authorize]
public sealed class LegalEntityController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<LegalEntityController> _logger;
    private readonly NotificationDispatcher _notifications;
    private readonly ContractWriter _contractWriter;

    public LegalEntityController(
        ApplicationDbContext dbContext,
        ILogger<LegalEntityController> logger,
        NotificationDispatcher notifications,
        ContractWriter contractWriter)
    {
        _dbContext = dbContext;
        _logger = logger;
        _notifications = notifications;
        _contractWriter = contractWriter;
    }

    [HttpGet("profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var entity = await _dbContext.LegalEntities
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId.Value, cancellationToken);

        return entity is null ? NotFound() : Ok(MapToDto(entity));
    }

    [HttpPost("profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpsertProfile([FromBody] UpsertLegalEntityRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required");

        if (string.IsNullOrWhiteSpace(request.Edrpou))
            return BadRequest("EDRPOU is required");

        // Format gate, not just non-empty: a 16-digit typo is not an EDRPOU (#128).
        var edrpouProblem = LegalEntityIdentifier.DescribeEdrpouProblem(request.Edrpou);
        if (edrpouProblem != null)
            return BadRequest(edrpouProblem);

        var vatProblem = LegalEntityIdentifier.DescribeVatProblem(request.VatNumber);
        if (vatProblem != null)
            return BadRequest(vatProblem);

        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var entity = await _dbContext.LegalEntities
            .FirstOrDefaultAsync(e => e.UserId == userId.Value, cancellationToken);

        bool isNewEntity = false;
        if (entity is null)
        {
            entity = new LegalEntity
            {
                Id = Guid.CreateVersion7(),
                UserId = userId.Value,
                CreatedAtUtc = SystemClock.UtcNow
            };
            _dbContext.LegalEntities.Add(entity);
            isNewEntity = true;
        }

        entity.Name = request.Name;
        entity.Edrpou = request.Edrpou;
        entity.VatNumber = request.VatNumber;
        entity.Address = request.Address;
        entity.DirectorName = request.DirectorName;
        entity.Phone = request.Phone;
        entity.Email = request.Email;
        entity.UpdatedAtUtc = SystemClock.UtcNow;

        if (!isNewEntity)
        {
            _dbContext.LegalEntities.Update(entity);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The EDRPOU is already registered to a different user. This endpoint upserts by
            // UserId, so the unique index that trips here is the one on legal_entities.edrpou —
            // surface it as a clean 409 rather than an unhandled 500 (mirrors Sign()'s Conflict).
            return Conflict("EDRPOU already registered");
        }

        // Only the initial creation is reported; this endpoint is an upsert and
        // profile edits are routine.
        if (isNewEntity)
        {
        await _notifications.CompanyCreatedAsync(entity.Id, entity.Name, entity.Edrpou, cancellationToken);

        // A company gets its contracts here, not by an operator's hand: the owner decided on
        // 2026-10-21 that a company is created with a contract for every provider, and the
        // client signs what already exists. The writer only adds what is missing, so calling
        // it again when a provider publishes a new text is the backfill too.
        await _contractWriter.EnsureContractsForAsync(entity, userId.Value, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        }

        return Ok(MapToDto(entity));
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

        var entities = await _dbContext.LegalEntities
            .AsNoTracking()
            .Where(e => e.UserId == userId.Value)
            .OrderBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(entities.Select(MapToDto));
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
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required");

        if (string.IsNullOrWhiteSpace(request.Edrpou))
            return BadRequest("EDRPOU is required");

        // Format gate, not just non-empty: a 16-digit typo is not an EDRPOU (#128).
        var edrpouProblem = LegalEntityIdentifier.DescribeEdrpouProblem(request.Edrpou);
        if (edrpouProblem != null)
            return BadRequest(edrpouProblem);

        var vatProblem = LegalEntityIdentifier.DescribeVatProblem(request.VatNumber);
        if (vatProblem != null)
            return BadRequest(vatProblem);

        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var entity = new LegalEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId.Value,
            Name = request.Name,
            Edrpou = request.Edrpou,
            VatNumber = request.VatNumber,
            Address = request.Address,
            DirectorName = request.DirectorName,
            Phone = request.Phone,
            Email = request.Email,
            CreatedAtUtc = SystemClock.UtcNow,
            UpdatedAtUtc = SystemClock.UtcNow
        };
        _dbContext.LegalEntities.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Duplicate legal_entities.edrpou (globally unique) — surface as a clean 409.
            return Conflict("EDRPOU already registered");
        }

        await _notifications.CompanyCreatedAsync(entity.Id, entity.Name, entity.Edrpou, cancellationToken);

        // A company gets its contracts here, not by an operator's hand: the owner decided on
        // 2026-10-21 that a company is created with a contract for every provider, and the
        // client signs what already exists. The writer only adds what is missing, so calling
        // it again when a provider publishes a new text is the backfill too.
        await _contractWriter.EnsureContractsForAsync(entity, userId.Value, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(MapToDto(entity));
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
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required");

        if (string.IsNullOrWhiteSpace(request.Edrpou))
            return BadRequest("EDRPOU is required");

        // Format gate, not just non-empty: a 16-digit typo is not an EDRPOU (#128).
        var edrpouProblem = LegalEntityIdentifier.DescribeEdrpouProblem(request.Edrpou);
        if (edrpouProblem != null)
            return BadRequest(edrpouProblem);

        var vatProblem = LegalEntityIdentifier.DescribeVatProblem(request.VatNumber);
        if (vatProblem != null)
            return BadRequest(vatProblem);

        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var entity = await _dbContext.LegalEntities
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId.Value, cancellationToken);

        if (entity is null)
            return NotFound("Legal entity not found");

        entity.Name = request.Name;
        entity.Edrpou = request.Edrpou;
        entity.VatNumber = request.VatNumber;
        entity.Address = request.Address;
        entity.DirectorName = request.DirectorName;
        entity.Phone = request.Phone;
        entity.Email = request.Email;
        entity.UpdatedAtUtc = SystemClock.UtcNow;
        _dbContext.LegalEntities.Update(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Duplicate legal_entities.edrpou (globally unique) — surface as a clean 409.
            return Conflict("EDRPOU already registered");
        }

        return Ok(MapToDto(entity));
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

        var contracts = await _dbContext.Contracts
            .AsNoTracking()
            .Include(c => c.Entity)
            .Include(c => c.Brand)
            .Where(c => c.UserId == userId.Value)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        // The text the customer is asked to read before signing. It lives once per provider and
        // the contract says which version it is against - so the text is read by that pair, and
        // a contract whose version has no text yet says so rather than showing an empty page.
        // The table holds one row per provider per version - four providers and a handful of
        // versions each - so it is read whole and matched here.
        var texts = (await _dbContext.ContractVersions
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .ToDictionary(v => (v.BrandId, v.Version), v => v.Body);

        var result = contracts.Select(c =>
        {
            texts.TryGetValue((c.BrandId, c.TermsVersion), out var body);
            return new
            {
                id = c.Id,
                legalEntityId = c.LegalEntityId,
                legalEntityName = c.Entity?.Name,
                brandId = c.BrandId,
                brandName = c.Brand?.Name,
                createdAt = c.CreatedAtUtc,
                // The validity term, and whether it has run out. A contract list that does not
                // say when a contract ends cannot answer "which of these is about to expire".
                expiresAt = c.ExpiresAtUtc,
                expired = c.ExpiresAtUtc <= SystemClock.UtcNow,
                // Which version of the provider's text this contract is against, and the text
                // itself. The mobile's "REVIEW THE TEXT" had nothing behind it before this.
                termsVersion = c.TermsVersion,
                termsText = body,
                hasText = body is not null
            };
        });

        return Ok(result);
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
        if (request.ContractId == Guid.Empty)
            return BadRequest("ContractId is required");

        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var contract = await _dbContext.Contracts
            .FirstOrDefaultAsync(c => c.Id == request.ContractId && c.UserId == userId.Value, cancellationToken);

        if (contract is null)
            return NotFound("Contract not found");

        // A contract that has already expired is not signed. The term is the whole point of
        // having one: without this check the expiry would be a field nobody reads, and the
        // signature would outlive the agreement it is a signature of.
        if (!ContractValidity.CanBeSigned(contract.ExpiresAtUtc, SystemClock.UtcNow))
            return BadRequest($"This contract expired on {contract.ExpiresAtUtc:yyyy-MM-dd} and cannot be signed.");

        var alreadySigned = await _dbContext.UserContracts
            .AnyAsync(uc => uc.ContractId == request.ContractId && uc.UserId == userId.Value, cancellationToken);

        if (alreadySigned)
            return Conflict("Contract already signed");

        var userContract = new UserContract
        {
            Id = Guid.CreateVersion7(),
            UserId = userId.Value,
            ContractId = request.ContractId,
            SignatureData = request.SignatureData,
            SignedAtUtc = SystemClock.UtcNow,
            // The version that was actually signed, frozen into the signature. A later version
            // of the terms does not rewrite what the customer read, and the audit trail has to
            // be able to say which one it was.
            TermsVersion = contract.TermsVersion
        };

        _dbContext.UserContracts.Add(userContract);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            id = userContract.Id,
            contractId = userContract.ContractId,
            signedAt = userContract.SignedAtUtc
        });
    }

    [HttpGet("contracts/signed")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSignedContracts(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var signed = await _dbContext.UserContracts
            .AsNoTracking()
            .Include(uc => uc.Contract)
                .ThenInclude(c => c!.Entity)
            .Include(uc => uc.Contract)
                .ThenInclude(c => c!.Brand)
            .Where(uc => uc.UserId == userId.Value)
            .OrderByDescending(uc => uc.SignedAtUtc)
            .ToListAsync(cancellationToken);

        var result = signed.Select(uc => new
        {
            id = uc.Id,
            contractId = uc.ContractId,
            legalEntityName = uc.Contract?.Entity?.Name,
            // The brand is carried by id, not only by name: a client that has to decide
            // "is this brand already signed?" needs something it can compare, and two
            // brands may share a name. The name is the label; the id is the fact.
            brandId = uc.Contract?.BrandId,
            brandName = uc.Contract?.Brand?.Name,
            signedAt = uc.SignedAtUtc,
            // A signature outlives the contract it is a signature of, so the term travels
            // with it - otherwise a signed-contract list cannot say which of them has run out.
            expiresAt = uc.Contract?.ExpiresAtUtc,
            expired = uc.Contract?.ExpiresAtUtc <= SystemClock.UtcNow
        });

        return Ok(result);
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private static object MapToDto(LegalEntity e) => new
    {
        id = e.Id,
        name = e.Name,
        edrpou = e.Edrpou,
        vatNumber = e.VatNumber,
        address = e.Address,
        directorName = e.DirectorName,
        phone = e.Phone,
        email = e.Email,
        createdAt = e.CreatedAtUtc,
        updatedAt = e.UpdatedAtUtc
    };
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
