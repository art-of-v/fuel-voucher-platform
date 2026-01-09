using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.Features.Users;

[ApiController]
[Route("api")]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _repository;

    public UsersController(IUserRepository repository)
    {
        _repository = repository;
    }

    [HttpGet("admin/users")]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _repository.GetAllAsync();
        return Ok(users);
    }

    [HttpPost("users/update")]
    public async Task<IActionResult> UpdateUser([FromBody] UpdateUserRequest request)
    {
        var userId = HttpContext.Session.GetString("userId") ?? "dev-user-123";
        var user = await _repository.UpdateAsync(userId, request);
        return Ok(user);
    }
}
