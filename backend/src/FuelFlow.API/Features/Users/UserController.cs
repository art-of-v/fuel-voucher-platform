using System.Security.Claims;
using FuelFlow.Features.Auth.DeleteUser;
using FuelFlow.Features.Users.ChangeEmail;
using FuelFlow.Features.Users.UpdateUser;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using static FuelFlow.API.Extensions.RateLimiterSetup;

namespace FuelFlow.Features.Users;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UserController : ControllerBase
{
    private readonly UpdateUserCommandHandler _updateUserHandler;
    private readonly RequestEmailChangeCommandHandler _requestEmailChangeHandler;
    private readonly DeleteUserCommandHandler _deleteUserHandler;

    public UserController(
        UpdateUserCommandHandler updateUserHandler,
        RequestEmailChangeCommandHandler requestEmailChangeHandler,
        DeleteUserCommandHandler deleteUserHandler)
    {
        _updateUserHandler = updateUserHandler;
        _requestEmailChangeHandler = requestEmailChangeHandler;
        _deleteUserHandler = deleteUserHandler;
    }

    [HttpPost("update")]
    [ProducesResponseType(typeof(UpdateUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found");

        var command = new UpdateUserCommand(
            userId,
            // Accepted for backward compatibility with older app builds but ignored: an email change
            // is now a step-up-guarded action on POST email/change, never a side effect of update.
            request.Email,
            request.FirstName,
            request.LastName,
            request.Birthdate,
            request.ProfileImageUrl,
            $"{Request.Scheme}://{Request.Host}"
        );

        var result = await _updateUserHandler.HandleAsync(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Begins a self-service email change. Requires a fresh OTP (requested via POST /api/auth/send-code
    /// and delivered to the account's current phone/email, never the new address) to re-prove identity
    /// before the change is staged; the new address is then confirmed by the link emailed to it. Closes
    /// the takeover vector where a stolen session silently repoints a staff member's login-OTP channel.
    /// </summary>
    [HttpPost("email/change")]
    [EnableRateLimiting(VerifyCodePolicy)]
    [ProducesResponseType(typeof(RequestEmailChangeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found");

        var command = new RequestEmailChangeCommand(
            userId,
            request.Email ?? string.Empty,
            request.Code ?? string.Empty,
            $"{Request.Scheme}://{Request.Host}"
        );

        try
        {
            var result = await _requestEmailChangeHandler.HandleAsync(command, cancellationToken);
            return Ok(result);
        }
        catch (StepUpChallengeFailedException ex)
        {
            // The session is valid; only the step-up OTP failed. Return 403 (not 401) with a stable
            // code so the mobile client shows "invalid code" and lets the user retry — a 401 here
            // trips the API client's refresh-then-clear-tokens path and logs them out for a typo.
            // Mirrors PurchaseController's AccountInactiveException → 403 handling.
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { code = StepUpChallengeFailedException.Code, message = ex.Message });
        }
    }

    [HttpDelete("me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAccount(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
            return Unauthorized("User ID not found");

        var result = await _deleteUserHandler.HandleAsync(new DeleteUserCommand(parsedUserId), cancellationToken);

        if (!result.Success)
            return NotFound(new { message = result.Error });

        return NoContent();
    }
}

public sealed class UpdateUserRequest
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateOnly? Birthdate { get; set; }
    public string? ProfileImageUrl { get; set; }
}

public sealed class ChangeEmailRequest
{
    /// <summary>The new email address to stage for confirmation.</summary>
    public string? Email { get; set; }

    /// <summary>Fresh OTP from POST /api/auth/send-code, proving control of the current account.</summary>
    public string? Code { get; set; }
}
