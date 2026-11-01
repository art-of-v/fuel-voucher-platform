using System.Security.Claims;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Errors;
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

            // IncludingSoftDeleted, and the explicit IsDeleted below, are both load-bearing.
            // users now carries a global `!IsDeleted` filter, so a soft-deleted account would be
            // invisible here and `account == null` would reject them - correct, but by accident
            // and for the wrong reason, and it would make this middleware unable to tell
            // "deleted" from "no such user" in the log. Selecting the flag explicitly says what
            // it means: a deleted account is rejected on purpose, not by accident.
            var account = await dbContext.Users
                .IncludingSoftDeleted()
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.IsDeleted, u.IsBanned, u.TokenVersion })
                .FirstOrDefaultAsync();

            if (account == null || account.IsDeleted || account.IsBanned)
            {
                _logger.LogWarning(
                    "Session rejected: user {UserId} not found, deleted or banned", userId);
                await Reject(context);
                return;
            }

            // Fail closed on a token that carries no readable version. The check below used to run
            // only when TryParse succeeded, so a token with the claim absent or unparsable skipped
            // the comparison entirely and was treated as current - which is every revocation
            // (ban, delete, role change, logout-everywhere) missed for exactly that token. Every
            // access token this API mints carries it (JwtTokenService.GenerateAccessToken), so
            // refusing without one costs no real session.
            var tokenVersionClaim = user.FindFirst("token_version")?.Value
                                 ?? user.FindFirst("tv")?.Value;
            if (!int.TryParse(tokenVersionClaim, out var tokenVersion))
            {
                _logger.LogWarning(
                    "Session rejected: no parsable token_version claim for user {UserId}", userId);
                await Reject(context);
                return;
            }

            if (tokenVersion != account.TokenVersion)
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
        await ApiErrorResponse.WriteAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized");
    }
}
