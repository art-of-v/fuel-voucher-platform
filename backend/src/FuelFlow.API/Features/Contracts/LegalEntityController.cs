using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FuelFlow.Features.Contracts;

[ApiController]
[Route("api/legal-entity")]
[Authorize]
public sealed class LegalEntityController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<LegalEntityController> _logger;

    public LegalEntityController(ApplicationDbContext dbContext, ILogger<LegalEntityController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
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
    public async Task<IActionResult> UpsertProfile([FromBody] UpsertLegalEntityRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required");

        if (string.IsNullOrWhiteSpace(request.Edrpou))
            return BadRequest("EDRPOU is required");

        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var entity = await _dbContext.LegalEntities
            .FirstOrDefaultAsync(e => e.UserId == userId.Value, cancellationToken);

        if (entity is null)
        {
            entity = new LegalEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                CreatedAtUtc = DateTime.UtcNow
            };
            _dbContext.LegalEntities.Add(entity);
        }

        entity.Name = request.Name;
        entity.Edrpou = request.Edrpou;
        entity.VatNumber = request.VatNumber;
        entity.Address = request.Address;
        entity.DirectorName = request.DirectorName;
        entity.Phone = request.Phone;
        entity.Email = request.Email;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(MapToDto(entity));
    }

    [HttpGet("contracts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetContracts(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var contracts = await _dbContext.Contracts
            .AsNoTracking()
            .Include(c => c.Entity)
            .Include(c => c.Station)
            .Where(c => c.UserId == userId.Value)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var result = contracts.Select(c => new
        {
            id = c.Id,
            legalEntityId = c.LegalEntityId,
            legalEntityName = c.Entity?.Name,
            stationId = c.StationId,
            stationName = c.Station?.Name,
            createdAt = c.CreatedAtUtc
        });

        return Ok(result);
    }

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

        var alreadySigned = await _dbContext.UserContracts
            .AnyAsync(uc => uc.ContractId == request.ContractId && uc.UserId == userId.Value, cancellationToken);

        if (alreadySigned)
            return Conflict("Contract already signed");

        var userContract = new UserContract
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            ContractId = request.ContractId,
            SignatureData = request.SignatureData,
            SignedAtUtc = DateTime.UtcNow
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
                .ThenInclude(c => c!.Station)
            .Where(uc => uc.UserId == userId.Value)
            .OrderByDescending(uc => uc.SignedAtUtc)
            .ToListAsync(cancellationToken);

        var result = signed.Select(uc => new
        {
            id = uc.Id,
            contractId = uc.ContractId,
            legalEntityName = uc.Contract?.Entity?.Name,
            stationName = uc.Contract?.Station?.Name,
            signedAt = uc.SignedAtUtc
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
