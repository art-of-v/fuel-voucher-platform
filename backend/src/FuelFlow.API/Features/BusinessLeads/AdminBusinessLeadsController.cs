using System.Security.Claims;
using FuelFlow.Features.BusinessLeads.GetAdminBusinessLeadById;
using FuelFlow.Features.BusinessLeads.GetAdminBusinessLeadFacets;
using FuelFlow.Features.BusinessLeads.GetAdminBusinessLeads;
using FuelFlow.Features.BusinessLeads.GetBusinessLeadAttentionCount;
using FuelFlow.Features.BusinessLeads.SharedModels;
using FuelFlow.Features.BusinessLeads.UpdateBusinessLeadStatus;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.BusinessLeads;

[ApiController]
[Route("api/admin/business-leads")]
public sealed class AdminBusinessLeadsController : ControllerBase
{
    private readonly GetAdminBusinessLeadsQueryHandler _list;
    private readonly GetAdminBusinessLeadByIdQueryHandler _detail;
    private readonly GetAdminBusinessLeadFacetsQueryHandler _facets;
    private readonly GetBusinessLeadAttentionCountQueryHandler _attention;
    private readonly UpdateBusinessLeadStatusCommandHandler _status;

    public AdminBusinessLeadsController(
        GetAdminBusinessLeadsQueryHandler list,
        GetAdminBusinessLeadByIdQueryHandler detail,
        GetAdminBusinessLeadFacetsQueryHandler facets,
        GetBusinessLeadAttentionCountQueryHandler attention,
        UpdateBusinessLeadStatusCommandHandler status)
    {
        _list = list;
        _detail = detail;
        _facets = facets;
        _attention = attention;
        _status = status;
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        if (!ValidStatusFilter(status))
            return ApiError.BadRequest($"Unknown business lead status '{status}'.", "business_lead_unknown_status");

        return Ok(await _list.HandleAsync(
            new GetAdminBusinessLeadsQuery(offset, limit, status, search, from, to), ct));
    }

    [HttpGet("facets")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetFacets(CancellationToken ct = default) =>
        Ok(await _facets.HandleAsync(new GetAdminBusinessLeadFacetsQuery(), ct));

    [HttpGet("attention/count")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetAttentionCount(CancellationToken ct = default) =>
        Ok(await _attention.HandleAsync(new GetBusinessLeadAttentionCountQuery(), ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _detail.HandleAsync(new GetAdminBusinessLeadByIdQuery(id), ct);
        return result is null ? ApiError.NotFound("Business lead not found.") : Ok(result);
    }

    [HttpPost("status")]
    [Authorize(Policy = AuthPolicies.StaffOperations)]
    public async Task<IActionResult> UpdateStatus(
        [FromBody] UpdateBusinessLeadStatusRequest request,
        CancellationToken ct = default)
    {
        var result = await _status.HandleAsync(
            new UpdateBusinessLeadStatusCommand(
                request.Ids,
                request.Status,
                CurrentUserId(),
                CurrentUserName()),
            ct);

        return result.Status switch
        {
            "NoIds" => ApiError.BadRequest(result.Error, "business_lead_no_ids"),
            "UnknownStatus" => ApiError.BadRequest(result.Error, "business_lead_unknown_status"),
            "Unauthenticated" => ApiError.Unauthorized("Unauthorized"),
            _ => Ok(new BusinessLeadMutationResponse(true, result.Affected))
        };
    }

    internal static bool ValidStatusFilter(string? status) =>
        string.IsNullOrWhiteSpace(status)
        || status.Equals("active", StringComparison.OrdinalIgnoreCase)
        || status.Equals("all", StringComparison.OrdinalIgnoreCase)
        || Enum.TryParse<BusinessLeadStatus>(status, true, out _);

    private Guid? CurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var parsed) ? parsed : null;
    }

    private string? CurrentUserName() =>
        User.FindFirst(ClaimTypes.Name)?.Value
        ?? User.FindFirst("first_name")?.Value;
}
