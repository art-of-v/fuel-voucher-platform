using Microsoft.AspNetCore.Mvc;
using FuelFlow.Api.Features.Users;

namespace FuelFlow.Api.Features.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepo;

    public AuthController(IUserRepository userRepo)
    {
        _userRepo = userRepo;
    }

    [HttpPost("dev-login")]
    public async Task<IActionResult> DevLogin()
    {
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production")
            return NotFound();

        var email = "dev@example.com";
        var user = await _userRepo.GetByEmailAsync(email);

        if (user == null)
        {
            user = await _userRepo.CreateWithEmailAsync(email);
        }

        HttpContext.Session.SetString("userId", user.Id);

        return Ok(new { success = true, user });
    }

    [HttpPost("phone/logout")]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return Ok(new { success = true });
    }
}
