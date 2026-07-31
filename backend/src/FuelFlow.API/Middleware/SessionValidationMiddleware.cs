using System.Security.Claims;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Middleware;

public sealed class SessionValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionValidationMiddleware> _logger;

    public SessionValidationMiddleware(
        RequestDelegate next,
        ILogger<SessionValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        var user = context.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                await Reject(context);
                return;
            }

            var account = await dbContext.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.IsActive, u.TokenVersion })
                .FirstOrDefaultAsync();

            if (account == null || !account.IsActive)
            {
                _logger.LogWarning("Session rejected: user {UserId} not found or inactive", userId);
                await Reject(context);
                return;
            }

            var tokenVersionClaim = user.FindFirst("token_version")?.Value;
            var tokenVersion = 0;
            if (string.IsNullOrEmpty(tokenVersionClaim) ||
                !int.TryParse(tokenVersionClaim, out tokenVersion) ||
                tokenVersion != account.TokenVersion)
            {
                _logger.LogWarning(
                    "Session rejected: token version mismatch for user {UserId} (token={TokenVersion}, expected={Expected})",
                    userId, tokenVersion, account.TokenVersion);
                await Reject(context);
                return;
            }
        }

        await _next(context);
    }

    private static async Task Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
    }
}
