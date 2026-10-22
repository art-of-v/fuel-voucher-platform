using System.Security.Claims;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Middleware;

/// <summary>
/// Turns the authenticated principal into the <see cref="TenantScope"/> the database policies
/// enforce.
/// </summary>
/// <remarks>
/// Mounted after <see cref="DeviceSignatureMiddleware"/>, because that middleware can add a
/// <see cref="ClaimsIdentity"/> for a request whose identity arrived with the device signature
/// rather than with the bearer token: the final identity is known only after it has run.
/// </remarks>
public sealed class TenantScopeMiddleware
{
    private readonly RequestDelegate _next;

    public TenantScopeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, TenantScopeAccessor accessor)
    {
        accessor.Set(Resolve(context));
        await _next(context);
    }

    internal static TenantScope Resolve(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<SystemScopeAttribute>() is not null)
        {
            return TenantScope.System;
        }

        var user = context.User;
        if (user.Identity?.IsAuthenticated == true
            && Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            return SeedRoles.StaffRoleNames.Any(user.IsInRole)
                ? TenantScope.System
                : TenantScope.ForUser(userId);
        }

        return TenantScope.Anonymous;
    }
}
