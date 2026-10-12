using System.Security.Claims;
using FuelFlow.Features.Referral.CreateReferralCode;
using FuelFlow.Features.Referral.RedeemReferralCode;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using static FuelFlow.API.Extensions.RateLimiterSetup;

namespace FuelFlow.Features.Referral;

[ApiController]
[Route("api/referral")]
[Authorize]
public sealed class ReferralController : ControllerBase
{
    private readonly CreateReferralCodeCommandHandler _createHandler;
    private readonly RedeemReferralCodeCommandHandler _redeemHandler;

    public ReferralController(
        CreateReferralCodeCommandHandler createHandler,
        RedeemReferralCodeCommandHandler redeemHandler)
    {
        _createHandler = createHandler;
        _redeemHandler = redeemHandler;
    }

    [HttpPost("create")]
    [EnableRateLimiting(ReferralWritePolicy)]
    [ProducesResponseType(typeof(CreateReferralCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateReferralCodeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest("Code is required");

        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var result = await _createHandler.HandleAsync(new CreateReferralCodeCommand(userId, request.Code), cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("redeem")]
    [EnableRateLimiting(ReferralWritePolicy)]
    [ProducesResponseType(typeof(RedeemReferralCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Redeem([FromBody] RedeemReferralCodeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest("Code is required");

        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var result = await _redeemHandler.HandleAsync(new RedeemReferralCodeCommand(userId, request.Code), cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private string? GetUserId() =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value;
}

public sealed class CreateReferralCodeRequest
{
    public string Code { get; set; } = null!;
}

public sealed class RedeemReferralCodeRequest
{
    public string Code { get; set; } = null!;
}
