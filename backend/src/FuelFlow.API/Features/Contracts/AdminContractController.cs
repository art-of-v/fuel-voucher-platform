using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts;

[ApiController]
[Route("api/admin/legal-entity/contracts")]
[Authorize(Roles = "Admin")]
public sealed class AdminContractController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminContractController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await _dbContext.Contracts
            .AsNoTracking()
            .Include(c => c.User)
            .Include(c => c.Entity)
            .Include(c => c.Station)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("signed-contracts")]
    public async Task<IActionResult> GetSignedContracts(CancellationToken cancellationToken)
    {
        var items = await _dbContext.UserContracts
            .AsNoTracking()
            .Include(uc => uc.User)
            .Include(uc => uc.Contract)
            .OrderByDescending(uc => uc.SignedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}
