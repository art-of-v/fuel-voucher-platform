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
                .Select(u => new { u.IsDeleted, u.TokenVersion })
                .FirstOrDefaultAsync();

            if (account == null || account.IsDeleted)
            {
                _logger.LogWarning("Session rejected: user {UserId} not found or deleted", userId);
                await Reject(context);
                return;
            }

            var tokenVersionClaim = user.FindFirst("token_version")?.Value
                                 ?? user.FindFirst("tv")?.Value;
            if (int.TryParse(tokenVersionClaim, out var tokenVersion) && tokenVersion != account.TokenVersion)
            {
                _logger.LogWarning("Session rejected: token version mismatch for user {UserId}", userId);
                await Reject(context);
                return;
            }

            // Exit early when the user exists and is authenticated.
            // Deliberately allow inactive users through at this layer so reads
            // (profile, vouchers, purchases list) stay alive during activation.
            // The buy/payment controllers apply their own IsActive check.
            await _next(context);
        }
        else
        {
            await _next(context);
        }
    }

    private static async Task Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
    }
}
