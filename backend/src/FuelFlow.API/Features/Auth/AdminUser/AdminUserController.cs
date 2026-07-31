using FuelFlow.Features.Auth.AdminUser.GetAdminUsers;
using FuelFlow.Features.Auth.DeleteUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Auth.AdminUser;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public sealed class AdminUserController : ControllerBase
{
    private readonly GetAdminUsersQueryHandler _handler;
    private readonly DeleteUserCommandHandler _deleteUserHandler;

    public AdminUserController(
        GetAdminUsersQueryHandler handler,
        DeleteUserCommandHandler deleteUserHandler)
    {
        _handler = handler;
        _deleteUserHandler = deleteUserHandler;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _handler.HandleAsync(new GetAdminUsersQuery(), ct));

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var userId))
            return BadRequest(new { message = "Invalid user id" });

        var result = await _deleteUserHandler.HandleAsync(new DeleteUserCommand(userId), cancellationToken);

        if (!result.Success)
            return NotFound(new { message = result.Error });

        return NoContent();
    }
}
