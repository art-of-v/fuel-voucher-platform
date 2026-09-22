using FuelFlow.Features.Auth.AdminUser.GetAdminUsers;
using FuelFlow.Features.Auth.AdminUser.SetUserActive;
using FuelFlow.Features.Auth.AdminUser.SetUserBanned;
using FuelFlow.Features.Auth.AdminUser.SetUserEmail;
using FuelFlow.Features.Auth.AdminUser.SetUserRole;
using FuelFlow.Features.Auth.DeleteUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FuelFlow.Features.Auth.AdminUser;

[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "Staff")]
public sealed class AdminUserController : ControllerBase
{
    private readonly GetAdminUsersQueryHandler _handler;
    private readonly SetUserActiveCommandHandler _setActiveHandler;
    private readonly SetUserRoleCommandHandler _setRoleHandler;
    private readonly SetUserBannedCommandHandler _setBannedHandler;
    private readonly SetUserEmailCommandHandler _setEmailHandler;
    private readonly DeleteUserCommandHandler _deleteUserHandler;

    public AdminUserController(
        GetAdminUsersQueryHandler handler,
        SetUserActiveCommandHandler setActiveHandler,
        SetUserRoleCommandHandler setRoleHandler,
        SetUserBannedCommandHandler setBannedHandler,
        SetUserEmailCommandHandler setEmailHandler,
        DeleteUserCommandHandler deleteUserHandler)
    {
        _handler = handler;
        _setActiveHandler = setActiveHandler;
        _setRoleHandler = setRoleHandler;
        _setBannedHandler = setBannedHandler;
        _setEmailHandler = setEmailHandler;
        _deleteUserHandler = deleteUserHandler;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? role,
        [FromQuery] bool? isActive,
        [FromQuery] string? q,
        CancellationToken ct = default) =>
        Ok(await _handler.HandleAsync(new GetAdminUsersQuery(role, isActive, q), ct));

    [HttpPost("{id}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate(string id, CancellationToken cancellationToken) =>
        await SetActiveInternal(id, true, cancellationToken);

    [HttpPost("{id}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(string id, CancellationToken cancellationToken) =>
        await SetActiveInternal(id, false, cancellationToken);

    [HttpPost("{id}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetRole(string id, [FromBody] SetUserRoleRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var userId))
            return BadRequest(new { message = "Invalid user id" });
        var (actingId, actingRole, actingName) = GetActor();
        if (!actingId.HasValue)
            return Unauthorized();
        var result = await _setRoleHandler.HandleAsync(
            new SetUserRoleCommand(userId, request.Role, actingId.Value, actingName, actingRole),
            cancellationToken);

        if (result.NotFound)
            return NotFound(new { message = result.Error });
        if (result.Forbidden)
            return Forbid();
        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return NoContent();
    }

    [HttpPost("{id}/email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetEmail(string id, [FromBody] SetUserEmailRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var userId))
            return BadRequest(new { message = "Invalid user id" });
        var (actingId, actingRole, actingName) = GetActor();
        if (!actingId.HasValue)
            return Unauthorized();

        // The confirmation link is built from the public origin this request arrived on.
        var confirmBaseUrl = $"{Request.Scheme}://{Request.Host}";
        var result = await _setEmailHandler.HandleAsync(
            new SetUserEmailCommand(userId, request.Email, actingId.Value, actingName, actingRole, confirmBaseUrl),
            cancellationToken);

        if (result.NotFound)
            return NotFound(new { message = result.Error });
        if (result.Forbidden)
            return Forbid();
        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(new { pendingConfirmation = result.PendingConfirmation });
    }

    [HttpPost("{id}/ban")]
    [Authorize(Policy = "AdminOrOwner")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ban(string id, CancellationToken cancellationToken) =>
        await SetBannedInternal(id, true, cancellationToken);

    [HttpPost("{id}/unban")]
    [Authorize(Policy = "AdminOrOwner")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unban(string id, CancellationToken cancellationToken) =>
        await SetBannedInternal(id, false, cancellationToken);

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOrOwner")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var userId))
            return BadRequest(new { message = "Invalid user id" });

        var (actingId, _, actingName) = GetActor();

        var result = await _deleteUserHandler.HandleAsync(
            new DeleteUserCommand(userId, actingId, actingName),
            cancellationToken);

        if (!result.Success)
            return NotFound(new { message = result.Error });

        return NoContent();
    }

    private async Task<IActionResult> SetActiveInternal(string id, bool isActive, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var userId))
            return BadRequest(new { message = "Invalid user id" });

        var (actingId, actingRole, actingName) = GetActor();
        if (!actingId.HasValue)
            return Unauthorized();

        var result = await _setActiveHandler.HandleAsync(
            new SetUserActiveCommand(userId, isActive, actingId.Value, actingName, actingRole),
            cancellationToken);

        if (result.NotFound)
            return NotFound(new { message = result.Error });
        if (result.Forbidden)
            return Forbid();
        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return NoContent();
    }

    private async Task<IActionResult> SetBannedInternal(string id, bool isBanned, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var userId))
            return BadRequest(new { message = "Invalid user id" });

        var (actingId, actingRole, actingName) = GetActor();
        if (!actingId.HasValue)
            return Unauthorized();

        var result = await _setBannedHandler.HandleAsync(
            new SetUserBannedCommand(userId, isBanned, actingId.Value, actingName, actingRole),
            cancellationToken);

        if (result.NotFound)
            return NotFound(new { message = result.Error });
        if (result.Forbidden)
            return Forbid();
        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return NoContent();
    }

    private (Guid? ActingUserId, string? ActingRole, string? ActingName) GetActor()
    {
        Guid? actingId = null;
        if (Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var parsedId))
            actingId = parsedId;

        var firstName = User.FindFirst("first_name")?.Value;
        var lastName = User.FindFirst("last_name")?.Value;
        var actingName = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(actingName))
            actingName = User.FindFirst(ClaimTypes.Name)?.Value;

        var actingRole = User.FindFirst(ClaimTypes.Role)?.Value;

        return (actingId, actingRole, actingName);
    }
}

public sealed class SetUserRoleRequest
{
    public string Role { get; set; } = null!;
}

public sealed class SetUserEmailRequest
{
    /// <summary>New email to verify, or null/empty to clear the address.</summary>
    public string? Email { get; set; }
}
