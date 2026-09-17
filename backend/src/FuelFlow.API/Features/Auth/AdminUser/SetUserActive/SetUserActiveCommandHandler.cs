using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FuelFlow.Features.Auth.AdminUser.SetUserActive;

public sealed class SetUserActiveCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SetUserActiveCommandHandler> _logger;
    private readonly ProviderEventService _eventService;

    public SetUserActiveCommandHandler(
        ApplicationDbContext context,
        ILogger<SetUserActiveCommandHandler> logger,
        ProviderEventService eventService)
    {
        _context = context;
        _logger = logger;
        _eventService = eventService;
    }

    public async Task<SetUserActiveResult> HandleAsync(
        SetUserActiveCommand command,
        CancellationToken cancellationToken)
    {
        var target = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);

        if (target is null)
            return SetUserActiveResult.CreateNotFound("User not found");

        // Hierarchy check — enforced server-side, never trust the SPA dropdown.
        if (!RoleHierarchy.CanActivateDeactivate(command.ActingRole, target.Role?.Name))
        {
            _logger.LogWarning(
                "User {TargetId} activate/deactivate rejected: actor role {ActingRole} cannot manage target role {TargetRole}",
                command.UserId, command.ActingRole, target.Role?.Name);
            return SetUserActiveResult.CreateForbidden();
        }

        if (target.IsActive == command.IsActive)
            return SetUserActiveResult.CreateSuccess(); // idempotent

        target.IsActive = command.IsActive;
        target.UpdatedAtUtc = DateTime.UtcNow;
        // Activation changes purchase eligibility, not the validity of the session.

        await _context.SaveChangesAsync(cancellationToken);

        await _eventService.RecordEventAsync(
            "User",
            target.Id.ToString(),
            command.IsActive ? "UserActivated" : "UserDeactivated",
            null,
            JsonSerializer.Serialize(new
            {
                targetId = target.Id,
                isActive = target.IsActive,
                tokenVersion = target.TokenVersion,
                actorRole = command.ActingRole
            }),
            command.ActingUserId,
            command.ActingUserName,
            (command.IsActive ? "Activated user " : "Deactivated user ") + target.PhoneNumber,
            command.ActingUserId.ToString(),
            cancellationToken);

        _logger.LogInformation(
            "User {TargetId} {Action} by {ActingUserId} ({ActingRole})",
            command.UserId, command.IsActive ? "activated" : "deactivated", command.ActingUserId, command.ActingRole);

        return SetUserActiveResult.CreateSuccess();
    }
}
