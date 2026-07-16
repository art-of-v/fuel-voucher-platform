using FuelFlow.Features.Contracts.GetAdminContracts;
using FuelFlow.Features.Contracts.GetSignedContracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Contracts;

[ApiController]
[Route("api/admin/legal-entity/contracts")]
[Authorize(Roles = "Admin")]
public sealed class AdminContractController : ControllerBase
{
    private readonly GetAdminContractsQueryHandler _getAll;
    private readonly GetSignedContractsQueryHandler _getSigned;

    public AdminContractController(GetAdminContractsQueryHandler getAll, GetSignedContractsQueryHandler getSigned)
    {
        _getAll = getAll;
        _getSigned = getSigned;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetAdminContractsQuery(), ct));

    [HttpGet("signed-contracts")]
    public async Task<IActionResult> GetSignedContracts(CancellationToken ct) =>
        Ok(await _getSigned.HandleAsync(new GetSignedContractsQuery(), ct));
}
