using System.Security.Claims;
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

    public UserController(UpdateUserCommandHandler updateUserHandler)
    {
        _updateUserHandler = updateUserHandler;
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
            request.ProfileImageUrl
        );

        var result = await _updateUserHandler.HandleAsync(command, cancellationToken);
        return Ok(result);
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
