using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FuelFlow.Features.Auth.QaTestAccess;

/// <summary>
/// Admin request to turn the QA test-access switch on or off. Authorization (Admin/ProductOwner
/// only) is enforced at the controller; this handler records who acted for the audit trail.
/// </summary>
public sealed record SetQaTestAccessCommand(
    bool Enabled,
    Guid ActingUserId,
    string? ActingUserName,
    string? ActingRole);

public sealed record SetQaTestAccessResult(bool PreviousEnabled, bool Enabled, int RevokedSessions);

/// <summary>
/// Flips the <c>QaTestAccess:Enabled</c> runtime setting and, on the ON→OFF transition, hard-revokes
/// the QA account's live sessions so "off" is immediate and total (spec §4): every outstanding
/// access token is invalidated by bumping the QA user's <c>TokenVersion</c>
/// (<see cref="FuelFlow.Middleware.SessionValidationMiddleware"/> enforces it per request), refresh
/// tokens and devices are revoked so no new access token can be minted at /refresh, and any
/// unredeemed QA verification codes are burned. Every change is written to the same audit outbox as
/// other privileged admin actions — actor, old value, new value, timestamp — and never records the
/// QA code. Reusing the setting store means there is no cache to go stale: the next auth attempt
/// reads the new value directly.
/// </summary>
public sealed class SetQaTestAccessCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly RuntimeSettingsService _settings;
    private readonly ProviderEventService _eventService;
    private readonly ILogger<SetQaTestAccessCommandHandler> _logger;
    private readonly FuelFlowMetrics? _metrics;

    public SetQaTestAccessCommandHandler(
        ApplicationDbContext context,
        RuntimeSettingsService settings,
        ProviderEventService eventService,
        ILogger<SetQaTestAccessCommandHandler> logger,
        FuelFlowMetrics? metrics = null)
    {
        _context = context;
        _settings = settings;
        _eventService = eventService;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<SetQaTestAccessResult> HandleAsync(SetQaTestAccessCommand command, CancellationToken cancellationToken)
    {
        var previous = await _settings.IsQaTestAccessEnabledAsync(cancellationToken);

        if (previous == command.Enabled)
        {
            // Idempotent: no audit noise, no needless re-revocation.
            return new SetQaTestAccessResult(previous, command.Enabled, RevokedSessions: 0);
        }

        await _settings.UpsertAsync(
            AppSettingKeys.QaTestAccessEnabled,
            command.Enabled.ToString(),
            command.ActingUserId,
            command.ActingUserName,
            cancellationToken);

        var revoked = 0;
        if (!command.Enabled)
        {
            revoked = await RevokeQaSessionsAsync(cancellationToken);
        }

        // OldValue/NewValue are jsonb columns, so they must carry valid JSON — never a bare "True".
        // These record only the switch state, the revocation count and the actor role; the QA code
        // and phone are deliberately absent.
        await _eventService.RecordEventAsync(
            "QaTestAccess",
            AppSettingKeys.QaTestAccessEnabled,
            command.Enabled ? "QaTestAccessEnabled" : "QaTestAccessDisabled",
            JsonSerializer.Serialize(new { enabled = previous }),
            JsonSerializer.Serialize(new { enabled = command.Enabled, revokedSessions = revoked, actorRole = command.ActingRole }),
            command.ActingUserId,
            command.ActingUserName,
            command.Enabled
                ? "Enabled QA test access"
                : $"Disabled QA test access (revoked {revoked} QA session(s))",
            command.ActingUserId.ToString(),
            cancellationToken);

        _metrics?.QaTestAccessSwitchChanged(command.Enabled);
        _logger.LogWarning(
            "QA test access {State} by {ActingUserId} ({ActingRole}); {Revoked} QA session(s) revoked",
            command.Enabled ? "ENABLED" : "DISABLED", command.ActingUserId, command.ActingRole, revoked);

        return new SetQaTestAccessResult(previous, command.Enabled, revoked);
    }

    /// <summary>
    /// Hard-revokes every live QA session. Targets accounts flagged <see cref="User.IsQaAccount"/>
    /// (normally exactly one). Mirrors the ban revocation path so the behavior is consistent and
    /// battle-tested. Also burns any unused QA verification codes so a code issued moments before
    /// the switch flipped cannot still be redeemed.
    /// </summary>
    private async Task<int> RevokeQaSessionsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // The context runs with global NoTracking, so every entity we intend to mutate must be
        // loaded AsTracking() or SaveChanges would silently persist nothing (mirrors
        // RuntimeSettingsService.UpsertAsync).
        var qaUsers = await _context.Users
            .AsTracking()
            .Where(u => u.IsQaAccount && !u.IsDeleted)
            .ToListAsync(cancellationToken);

        var revokedTokens = 0;
        foreach (var qaUser in qaUsers)
        {
            qaUser.TokenVersion++;
            qaUser.UpdatedAtUtc = now;

            var refreshTokens = await _context.RefreshTokens
                .AsTracking()
                .Where(rt => rt.UserId == qaUser.Id && !rt.IsRevoked)
                .ToListAsync(cancellationToken);
            foreach (var token in refreshTokens)
            {
                token.IsRevoked = true;
                token.RevokedAtUtc = now;
            }
            revokedTokens += refreshTokens.Count;

            var devices = await _context.Devices
                .AsTracking()
                .Where(d => d.UserId == qaUser.Id && d.Status == DeviceStatus.Active)
                .ToListAsync(cancellationToken);
            foreach (var device in devices)
            {
                device.Status = DeviceStatus.Revoked;
            }

            // Burn any outstanding (unused, unexpired) QA verification codes.
            var liveCodes = await _context.VerificationCodes
                .AsTracking()
                .Where(v => v.PhoneNumber == qaUser.PhoneNumber && !v.IsUsed)
                .ToListAsync(cancellationToken);
            foreach (var vc in liveCodes)
            {
                vc.IsUsed = true;
                vc.UsedAtUtc = now;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return revokedTokens;
    }
}
