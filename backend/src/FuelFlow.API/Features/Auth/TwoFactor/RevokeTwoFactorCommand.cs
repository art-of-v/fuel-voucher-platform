using FuelFlow.Features.Auth.Logout;
using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FuelFlow.Features.Auth.TwoFactor;

public sealed record RevokeTwoFactorCommand(Guid UserId, Guid ActingUserId, string? ActingName);

public sealed record RevokeTwoFactorResult(bool Success, bool NotFound, string? Error);

/// <summary>
/// Takes the second factor away from a staff account.
/// </summary>
/// <remarks>
/// <para>
/// This is the recovery path: the authenticator phone was lost, or the member leaves and the
/// phone stays. Without it, a staff member whose phone is gone cannot sign in at all - which is
/// the safe direction to fail in, but it still needs a way out that does not involve editing the
/// database by hand.
/// </para>
/// <para>
/// The sessions of that account end with the factor, in one transaction. A factor that is
/// revoked in one transaction and audited in another leaves a window in which the change
/// happened and the trail does not say so - and a factor that is revoked while the sessions it
/// protected stay alive would be a factor revoked in name only: whoever holds the old token
/// keeps the access it was minted for, until it expires on its own.
/// </para>
/// </remarks>
public sealed class RevokeTwoFactorCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly StaffTwoFactorService _twoFactor;
    private readonly LogoutEverywhereCommandHandler _logoutEverywhere;
    private readonly AuditEventService _eventService;
    private readonly ILogger<RevokeTwoFactorCommandHandler> _logger;

    public RevokeTwoFactorCommandHandler(
        ApplicationDbContext context,
        StaffTwoFactorService twoFactor,
        LogoutEverywhereCommandHandler logoutEverywhere,
        AuditEventService eventService,
        ILogger<RevokeTwoFactorCommandHandler> logger)
    {
        _context = context;
        _twoFactor = twoFactor;
        _logoutEverywhere = logoutEverywhere;
        _eventService = eventService;
        _logger = logger;
    }

    public async Task<RevokeTwoFactorResult> HandleAsync(
        RevokeTwoFactorCommand command,
        CancellationToken cancellationToken)
    {
        // Tracked: the account and its factor are changed here, and the guard that reads this
        // file wants the persistence intent to be visible.
        var user = await _context.Users
            .AsTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);

        if (user == null)
            return new RevokeTwoFactorResult(false, NotFound: true, "User not found");

        // Atomic: the factor is taken away, the sessions die and the audit row lands - or none
        // of it happens. The transaction runs through ReplaySafeTransaction, so the execution
        // strategy may replay it: a transient failure retries the body rather than failing the
        // request, and the retry writes the audit row once.
        var hadFactor = false;

        await _context.ExecuteReplaySafeAsync(async ct =>
        {
            // Idempotent: an account that never had a second factor is already in the state the
            // caller asked for, and saying so is more useful than a failure.
            hadFactor = await _twoFactor.RevokeAsync(user.Id, ct);

            // The sessions go with the factor - see the remarks on this class.
            await _logoutEverywhere.HandleAsync(new LogoutEverywhereCommand(user.Id), ct);

            _eventService.RecordEvent(
                "Auth",
                user.Id.ToString(),
                "StaffTwoFactorRevoked",
                null,
                JsonSerializer.Serialize(new { user.Id, user.PhoneNumber, Role = user.Role?.Name, HadFactor = hadFactor }),
                command.ActingUserId,
                command.ActingName,
                $"The second factor of {user.PhoneNumber} was revoked",
                user.Id.ToString());

            await _context.SaveChangesAsync(ct);
        }, cancellationToken: cancellationToken);

        // Logged after the commit, so a transaction that never landed does not leave a line
        // saying the factor was revoked.
        _logger.LogInformation(
            "The second factor of user {UserId} was revoked by {ActingUserId} (had one: {HadFactor})",
            user.Id,
            command.ActingUserId,
            hadFactor);

        return new RevokeTwoFactorResult(true, NotFound: false, null);
    }
}
