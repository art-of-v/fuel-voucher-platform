using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FuelFlow.Features.Auth.AdminUser.SetPromoAccess;

/// <summary>
/// Writes one customer's access to the promotion, and the audit row that says who did it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the endpoint exists at all.</b> The owner's ruling of 2026-11-06 splits the customers into
/// two groups: those who may be served on the promotional route and those who may not, with a normal
/// customer in the second group and nobody below a Manager able to move anyone into the first. The
/// mobile app shows a second button because of this column, but the button is not the check — an order
/// created on the promotional route is refused here at creation, so a hand-built request cannot buy
/// what was not granted.
/// </para>
/// <para>
/// <b>What the access actually opens.</b> A route where the money is a person's word in a closed
/// channel rather than a captured card payment, and where no fiscal document is issued. That is why the
/// write is audited as a decision and not logged as a field edit: afterwards, the question an accountant
/// asks is not "was the flag true" but "who said this customer may be paid that way, and when".
/// </para>
/// <para>
/// <b>Only a customer account may hold it.</b> A staff account with the flag set would be a person able
/// to have themselves served outside the acquirer — the one shape the money-first rule of the order is
/// there to refuse, reached from the other side. So the refusal is not a hierarchy rule
/// (<see cref="RoleHierarchy"/>) but a type rule: the grant belongs to a <c>User</c> row. An account
/// with no role is refused too, and the sentence says what to do about it, because a roleless row is
/// already unreachable through the rest of this surface (planning #299).
/// </para>
/// <para>
/// <b>No self-grant.</b> The two-person rule of the channel — finance declares, somebody else serves —
/// has to survive the switch that decides who may be declared for. A Manager granting themselves the
/// promotion is one click from a sale with no acquirer and no second person in it.
/// </para>
/// <para>
/// <b>What revoking does not do.</b> It closes the door, not the room: a promotional order already
/// created keeps its money and keeps being fulfillable, because the declaration that pays for it was
/// made while the access stood, and taking the access away afterwards would strand a paid order over a
/// bookkeeping preference. The next checkout is what the revocation answers.
/// </para>
/// </remarks>
public sealed class SetPromoAccessCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SetPromoAccessCommandHandler> _logger;
    private readonly AuditEventService _eventService;

    public SetPromoAccessCommandHandler(
        ApplicationDbContext context,
        ILogger<SetPromoAccessCommandHandler> logger,
        AuditEventService eventService)
    {
        _context = context;
        _logger = logger;
        _eventService = eventService;
    }

    public async Task<SetPromoAccessResult> HandleAsync(
        SetPromoAccessCommand command,
        CancellationToken cancellationToken)
    {
        // AsTracking, stated rather than inherited: this mutates the row, and under the project's
        // NoTracking default a tracked-looking query is a silent no-op that still reports success.
        var target = await _context.Users
            .AsTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);

        if (target is null)
            return SetPromoAccessResult.CreateNotFound("User not found");

        if (command.ActingUserId == target.Id)
        {
            _logger.LogWarning(
                "Promotion access refused: staff {ActingId} ({ActingRole}) tried to set it on their own account.",
                command.ActingUserId, command.ActingRole);
            return SetPromoAccessResult.CreateForbidden(
                "You cannot set promotion access on your own account. Somebody else decides who is "
                + "served outside the acquirer.");
        }

        var targetRole = target.Role?.Name;
        if (targetRole != SeedRoles.UserName)
        {
            var reason = targetRole is null
                ? "This account has no role, so it is not a customer account yet. Give it one first."
                : $"A {targetRole} account is staff, and staff cannot be granted promotion access.";

            _logger.LogWarning(
                "Promotion access refused for user {TargetId}: {Reason}", command.UserId, reason);
            return SetPromoAccessResult.CreateForbidden(reason);
        }

        var hasIt = target.PromoAccessGrantedAtUtc is not null;
        if (hasIt == command.Granted)
        {
            // The answer is the state, not the write: an operator who turns a switch that is already
            // turned is told so, and no audit row claims a decision nobody made.
            return SetPromoAccessResult.Create(hasIt, target.PromoAccessGrantedAtUtc);
        }

        target.PromoAccessGrantedAtUtc = command.Granted ? SystemClock.UtcNow : null;
        target.UpdatedAtUtc = SystemClock.UtcNow;

        _eventService.RecordEvent(
            "User",
            target.Id.ToString(),
            command.Granted ? "PromoAccessGranted" : "PromoAccessRevoked",
            null,
            JsonSerializer.Serialize(new
            {
                targetId = target.Id,
                granted = command.Granted,
                grantedAtUtc = target.PromoAccessGrantedAtUtc,
                actorRole = command.ActingRole,
            }),
            command.ActingUserId,
            command.ActingUserName,
            (command.Granted ? "Granted promotion access to user " : "Revoked promotion access from user ")
            + target.PhoneNumber,
            command.ActingUserId.ToString());

        // One save for the column and the audit row: the row and the change it describes commit
        // together, or neither does (planning #533).
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "PROMO ACCESS: user {TargetId} {Action} by {ActingUserId} ({ActingRole})",
            command.UserId, command.Granted ? "granted" : "revoked", command.ActingUserId, command.ActingRole);

        return SetPromoAccessResult.Create(command.Granted, target.PromoAccessGrantedAtUtc);
    }
}
