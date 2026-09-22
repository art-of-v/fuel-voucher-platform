using System.Security.Claims;
using FuelFlow.Features.Auth.DeleteUser;
using FuelFlow.Features.Users.UpdateUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Users;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UserController : ControllerBase
{
    private readonly UpdateUserCommandHandler _updateUserHandler;
    private readonly DeleteUserCommandHandler _deleteUserHandler;

    public UserController(
        UpdateUserCommandHandler updateUserHandler,
        DeleteUserCommandHandler deleteUserHandler)
    {
        _updateUserHandler = updateUserHandler;
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
            request.Email,
            request.FirstName,
            request.LastName,
            request.Birthdate,
            request.ProfileImageUrl,
            // Built from the origin the request arrived on; used only when Email is a change, to
            // construct the confirmation link sent to the new address.
            $"{Request.Scheme}://{Request.Host}"
        );

        var result = await _updateUserHandler.HandleAsync(command, cancellationToken);
        return Ok(result);
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
