using FuelFlow.Features.Auth.AdminUser.GetAdminUsers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Auth.AdminUser;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public sealed class AdminUserController : ControllerBase
{
    private readonly GetAdminUsersQueryHandler _handler;

    public AdminUserController(GetAdminUsersQueryHandler handler) => _handler = handler;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _handler.HandleAsync(new GetAdminUsersQuery(), ct));
}
