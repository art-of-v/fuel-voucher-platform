using System.Security.Claims;
using FuelFlow.Features.Contracts.GetAdminContracts;
using FuelFlow.Features.Contracts.GetSignedContractSignature;
using FuelFlow.Features.Contracts.GetSignedContracts;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Contracts;

[ApiController]
[Route("api/admin/legal-entity/contracts")]
public sealed class AdminContractController : ControllerBase
{
    private readonly GetAdminContractsQueryHandler _getAll;
    private readonly GetSignedContractsQueryHandler _getSigned;
    private readonly GetSignedContractSignatureQueryHandler _getSignature;

    public AdminContractController(
        GetAdminContractsQueryHandler getAll,
        GetSignedContractsQueryHandler getSigned,
        GetSignedContractSignatureQueryHandler getSignature)
    {
        _getAll = getAll;
        _getSigned = getSigned;
        _getSignature = getSignature;
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.Staff)]
    [ProducesResponseType(typeof(List<AdminContractDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetAdminContractsQuery(), ct));

    /// <summary>
    /// A page of the signatures on the platform - who signed what, and when.
    /// </summary>
    /// <remarks>
    /// The list carries no signature image and is a page, not the whole table. A handwritten
    /// signature cannot be revoked and cannot be rotated, so it is a capability of its own
    /// (<see cref="AuthPolicies.SignaturePayload"/>), and the image is served one record at a
    /// time by <see cref="GetSignedContractSignature"/>, which records who opened it
    /// (planning #622, S-13).
    /// </remarks>
    [HttpGet("signed-contracts")]
    [Authorize(Policy = AuthPolicies.SignaturePayload)]
    [ProducesResponseType(typeof(PagedResult<AdminSignedContractDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSignedContracts(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        CancellationToken ct = default) =>
        Ok(await _getSigned.HandleAsync(new GetSignedContractsQuery(page, limit), ct));

    /// <summary>
    /// One customer's handwritten signature - and the audit row that says who asked for it.
    /// </summary>
    [HttpGet("signed-contracts/{id:guid}")]
    [Authorize(Policy = AuthPolicies.SignaturePayload)]
    [ProducesResponseType(typeof(AdminSignedContractSignatureDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSignedContractSignature([FromRoute] Guid id, CancellationToken ct)
    {
        // The endpoint writes an audit row, and a row that cannot name its actor is a row
        // nobody can answer for afterwards. A token this service did not mint carries no id, and
        // an unattributed look at a biometric artefact is refused rather than performed
        // anonymously.
        var userId = GetUserId();
        if (userId is null)
            return ApiError.Unauthorized("The token carries no user, so the signature could not be attributed.");

        var signature = await _getSignature.HandleAsync(
            new GetSignedContractSignatureQuery(id, userId.Value, GetUserName()), ct);

        return signature is null
            ? ApiError.NotFound("There is no signature with that id.")
            : Ok(signature);
    }

    /// <summary>
    /// The authenticated staff member's id, or <c>null</c> when the token carries none.
    /// </summary>
    private Guid? GetUserId()
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(value, out var id) ? id : null;
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
