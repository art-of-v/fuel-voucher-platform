namespace FuelFlow.Features.Users.ChangeEmail;

/// <summary>
/// Self-service email-change initiation, guarded by a fresh OTP step-up. <paramref name="Code"/> is
/// a one-time code the user just requested via POST /api/auth/send-code, which is delivered to the
/// account's CURRENT phone (or current email, for staff) — never to the new address — so someone who
/// only controls the new mailbox cannot start a change. On success the new address is staged as a
/// pending change and a confirmation link is emailed to it; the active email is untouched until that
/// link is opened (GET /api/auth/email/confirm).
/// </summary>
public sealed record RequestEmailChangeCommand(
    string UserId,
    string NewEmail,
    string Code,
    string ConfirmBaseUrl);

public sealed record RequestEmailChangeResponse(bool EmailChangePending = true);
