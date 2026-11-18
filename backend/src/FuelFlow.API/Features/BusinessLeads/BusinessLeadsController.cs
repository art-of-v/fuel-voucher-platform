using FuelFlow.API.Extensions;
using FuelFlow.Features.BusinessLeads.CreateBusinessLead;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FuelFlow.Features.BusinessLeads;

[ApiController]
[Route("api/business-leads")]
[AllowAnonymous]
public sealed class BusinessLeadsController : ControllerBase
{
    private readonly CreateBusinessLeadCommandHandler _handler;

    public BusinessLeadsController(CreateBusinessLeadCommandHandler handler) => _handler = handler;

    [HttpPost]
    [EnableRateLimiting(RateLimiterSetup.BusinessLeadPolicy)]
    [ProducesResponseType(typeof(CreateBusinessLeadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Create(
        CreateBusinessLeadCommand command,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.Website))
            return NoContent();

        var errors = CreateBusinessLeadCommandValidator.Validate(command);
        if (errors.Count > 0)
            return ApiError.Validation(errors);

        var userAgent = Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
        var result = await _handler.HandleAsync(
            command,
            userAgent,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        return Accepted(result);
    }
}
