using FuelFlow.Features.Auth.QaTestAccess;
using FuelFlow.Features.Auth.Security;
using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Notifications.Push;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Notifications.Email;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace FuelFlow.Features.Auth.SendCode;

public sealed record SendCodeCommand(string PhoneNumber);

/// <summary>
/// The answer to a send-code call. <paramref name="ExpiresInSeconds"/> is the code's lifetime,
/// the same public constant for every account - it tells the client when the digits it is about
/// to show stop being accepted (the code step measures that locally and offers a resend instead
/// of a dead end), and it reveals nothing about the account itself.
/// </summary>
public sealed record SendCodeResponse(
    bool Success,
    int ExpiresInSeconds = SendCodeCommandHandler.CodeTtlSeconds);

public sealed class SendCodeCommandHandler
{
    /// <summary>
    /// How long a sign-in code stays valid (spec §2). The DB row's deadline, the response field
    /// and the push payload all read this one value, so the client can never disagree with the
    /// server about when a code died.
    /// </summary>
    public const int CodeTtlSeconds = 5 * 60;

    private static readonly TimeSpan CodeTtl = TimeSpan.FromSeconds(CodeTtlSeconds);

    private readonly ApplicationDbContext _context;
    private readonly ISmsService _smsService;
    private readonly IPhoneNumberService _phoneNumberService;
    private readonly ILogger<SendCodeCommandHandler> _logger;
    private readonly IOptions<AuthOptions> _authOptions;
    private readonly IEmailSender _emailSender;
    private readonly IExpoPushSender? _pushSender;
    private readonly AuditEventService _eventService;
    private readonly QaTestAccessService? _qaTestAccess;
    private readonly FuelFlowMetrics? _metrics;

    private const string AdminEmailSubject = "FuelFlow admin sign-in code";

    public SendCodeCommandHandler(
        ApplicationDbContext context,
        ISmsService smsService,
        IPhoneNumberService phoneNumberService,
        ILogger<SendCodeCommandHandler> logger,
        IOptions<AuthOptions> authOptions,
        IEmailSender emailSender,
        AuditEventService eventService,
        QaTestAccessService? qaTestAccess = null,
        FuelFlowMetrics? metrics = null,
        IExpoPushSender? pushSender = null)
    {
        _context = context;
        _smsService = smsService;
        _phoneNumberService = phoneNumberService;
        _logger = logger;
        _authOptions = authOptions;
        _emailSender = emailSender;
        _pushSender = pushSender;
        _eventService = eventService;
        _qaTestAccess = qaTestAccess;
        _metrics = metrics;
    }

    public async Task<SendCodeResponse> HandleAsync(SendCodeCommand command, CancellationToken cancellationToken)
    {
        var phoneNumber = _phoneNumberService.Normalize(command.PhoneNumber);

        var unusedCodes = await _context.VerificationCodes
            .Where(v => v.PhoneNumber == phoneNumber && !v.IsUsed)
            .ToListAsync(cancellationToken);

        foreach (var existingCode in unusedCodes)
        {
            existingCode.IsUsed = true;
        }
        _context.VerificationCodes.UpdateRange(unusedCodes);

        // QA branch: only for the seeded QA phone, and only while QA access is configured AND the
        // runtime switch is on. We store the pre-computed QA code hash directly (the plaintext is
        // never held anywhere) and skip SMS entirely — no brand cost, works before the gateway
        // is live, and the fixed code never rides an SMS. Any other phone, or QA disabled, falls
        // through to the ordinary random-code path below, so the QA number is indistinguishable
        // from any other number when the feature is off (no enumeration oracle, spec §7).
        if (_qaTestAccess is not null
            && _qaTestAccess.IsQaPhone(phoneNumber)
            && await _qaTestAccess.IsActiveAsync(cancellationToken))
        {
            _context.VerificationCodes.Add(new VerificationCode
            {
                Id = Guid.CreateVersion7(),
                PhoneNumber = phoneNumber,
                Code = _qaTestAccess.CodeHash!, // already a peppered hash of the configured QA code
                ExpiresAtUtc = SystemClock.UtcNow.Add(CodeTtl),
                CreatedAtUtc = SystemClock.UtcNow,
                IsUsed = false
            });
            await _context.SaveChangesAsync(cancellationToken);

            // No code, ever, in the log line.
            _logger.LogInformation("QA test-access code issued for the QA account; no SMS sent");
            _metrics?.QaTestAccessSendCode();

            return new SendCodeResponse(true, CodeTtlSeconds);
        }

        var code = ResolveCode();
        var verificationCode = new VerificationCode
        {
            Id = Guid.CreateVersion7(),
            PhoneNumber = phoneNumber,
            Code = OtpCodeHasher.Hash(code, _authOptions.Value.OtpPepper),
            ExpiresAtUtc = SystemClock.UtcNow.Add(CodeTtl), // spec §2: verification codes valid for 5 minutes
            CreatedAtUtc = SystemClock.UtcNow,
            IsUsed = false
        };

        _context.VerificationCodes.Add(verificationCode);
        await _context.SaveChangesAsync(cancellationToken);

        await DeliverCodeAsync(phoneNumber, code, cancellationToken);

        return new SendCodeResponse(true, CodeTtlSeconds);
    }

    /// <summary>
    /// Picks the channel the code rides on, strongest and cheapest first (spec §3/§15):
    /// push to the account's own devices, then the email break-glass, then the SIM. The push
    /// channel is a deployment switch and needs an active device token; the break-glass is off
    /// by default and has to be acknowledged, recorded and earned. Any gap in the chosen path -
    /// disabled, not configured, no token or email on file, or a send failure - falls back to
    /// SMS, so a staff member is never locked out and a brand-new or not-yet-activated user
    /// always receives their code. IsActive gates only voucher purchase, never OTP delivery,
    /// so it is not consulted here.
    /// </summary>
    private async Task DeliverCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        // Push before the break-glass: when both are available the push is the stronger channel
        // (device-bound, free) and the mailbox is neither, so the acknowledged-weaker path stays
        // for accounts with no registered device.
        if (_authOptions.Value.OtpDeliveryViaPush
            && await TryDeliverViaPushAsync(phoneNumber, code, cancellationToken))
        {
            return;
        }

        if (BreakGlassIsOpen() && _emailSender.IsConfigured)
        {
            var staff = await TryResolveStaffAsync(phoneNumber, cancellationToken);
            if (staff is not null && !string.IsNullOrWhiteSpace(staff.Email))
            {
                // The break-glass weakens the FIRST factor, so it is only offered to an account
                // that still has to prove a second one. For an account without a second factor
                // the mailbox would be the whole sign-in, and that is the finding this closes.
                var hasSecondFactor = await _context.StaffTwoFactors
                    .AsNoTracking()
                    .AnyAsync(f => f.UserId == staff.Id && f.SecretCiphertext != null, cancellationToken);

                if (hasSecondFactor)
                {
                    try
                    {
                        await _emailSender.SendAsync(staff.Email!, BuildAdminEmail(code), cancellationToken);
                        await RecordBreakGlassUseAsync(staff, cancellationToken);
                        _logger.LogWarning(
                            "Staff verification code emailed to the account for {PhoneNumber} through the break-glass",
                            SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                            throw;
                        _logger.LogWarning(ex,
                            "Staff OTP email failed for {PhoneNumber}; falling back to SMS",
                            SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "The email break-glass was not offered to {PhoneNumber}: the account has no second factor",
                        SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
                }
            }
        }

        await _smsService.SendVerificationCodeAsync(phoneNumber, code, cancellationToken);
        _logger.LogInformation("Verification code sent to {PhoneNumber}",
            SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
    }

    /// <summary>
    /// Delivers the sign-in code to the account's own devices over push (Expo), when the
    /// backend runs with the push channel on and the account has an active device token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An SMS costs money and the brand balance is finite; when it runs out the budget guard
    /// refuses every send, so nobody can sign in at all. A push costs nothing, and the platform
    /// already ships one to every device that registered its token.
    /// </para>
    /// <para>
    /// The audience is the account's devices, never the device that asked for the code - so a
    /// code cannot be handed to whoever happens to hold the requesting phone. A device that
    /// signed out keeps its token (it is parked, not dead), because a signed-out device is
    /// exactly where a worker signs back in from.
    /// </para>
    /// <para>
    /// It is a fallback, not a replacement: an account with no active token, or a push Expo
    /// rejects on every device, still gets its code over SMS. That is what keeps a device that
    /// never registered - or one whose token went stale - from being locked out.
    /// </para>
    /// <para>
    /// The payload is the sign-in contract: <c>type: "auth_otp"</c>, the six-digit <c>code</c>,
    /// the <c>phone</c> it was issued for and the <c>ttlSeconds</c> it stays valid. Together they
    /// take a tap to <c>/landing?otpPhone=…&amp;otpCode=…&amp;otpExpiresAt=…</c>, where the last
    /// value is measured on the device (the notification's own delivery time plus the lifetime);
    /// see <c>mobile/src/core/notifications/notificationResponse.ts</c>.
    /// </para>
    /// </remarks>
    /// <returns>True when the code went out over push; false when SMS has to carry it.</returns>
    private async Task<bool> TryDeliverViaPushAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        if (_pushSender is null)
            return false;

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber && !u.IsDeleted, cancellationToken);

        if (user is null)
            return false;

        var tokens = await _context.PushTokens
            .AsNoTracking()
            .Where(t => t.UserId == user.Id && t.IsActive)
            .Select(t => new { t.Id, t.Token })
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0)
            return false;

        var data = new Dictionary<string, object>
        {
            ["type"] = "auth_otp",
            ["code"] = code,
            ["phone"] = phoneNumber,
            // How long the digits live. The tap path measures the age of the notification itself
            // (same device clock on both ends), so a banner left in Notification Center past the
            // deadline opens the form knowing the code is dead instead of failing on submit.
            ["ttlSeconds"] = CodeTtlSeconds
        };

        // Ukrainian, like the SMS copy and the other pushes the platform sends.
        var messages = tokens
            .Select(token => new ExpoPushMessage(token.Token, "FuelFlow", $"Ваш код для входу: {code}", data))
            .ToList();

        var results = await _pushSender.SendAsync(messages, cancellationToken);

        // A token Expo reports as gone (the app was uninstalled, the token rotated) is a phone
        // that is not there any more - stop counting it as an audience, and stop paying Expo
        // for the error on every send.
        var dead = results
            .Where(r => r.Status == ExpoPushStatus.Error
                        && string.Equals(r.ErrorCode, "DeviceNotRegistered", StringComparison.Ordinal))
            .Select(r => r.Token)
            .ToHashSet(StringComparer.Ordinal);

        if (dead.Count > 0)
            await DeactivateDeadTokensAsync(user.Id, dead, cancellationToken);

        // One accepted push is a delivered code. It used to require EVERY device to accept, and
        // that rule made a single dead phone push the whole sign-in back onto the paid SMS
        // channel - even when the code had already reached the device in the user's hand, and
        // even when the SMS budget was empty and the fallback could not be delivered at all.
        var delivered = results.Count(r => r.Status == ExpoPushStatus.Ok);
        if (delivered == 0)
        {
            _logger.LogWarning(
                "Push delivery of the verification code to {DeviceCount} device(s) for {PhoneNumber} did not succeed; falling back to SMS",
                tokens.Count,
                SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
            return false;
        }

        _logger.LogInformation(
            "Verification code sent by push to {DeviceCount} device(s) for {PhoneNumber}",
            delivered,
            SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
        return true;
    }

    /// <summary>
    /// Marks the tokens Expo reports as unregistered as dead, so the next sign-in does not try
    /// them again. Scoped to the account: a token of somebody else's is never touched.
    /// </summary>
    private async Task DeactivateDeadTokensAsync(
        Guid userId,
        HashSet<string> deadTokens,
        CancellationToken cancellationToken)
    {
        var rows = await _context.PushTokens
            .Where(t => t.UserId == userId && deadTokens.Contains(t.Token))
            .ToListAsync(cancellationToken);

        var now = SystemClock.UtcNow;
        foreach (var row in rows)
        {
            row.IsActive = false;
            row.UpdatedAtUtc = now;
            // Global NoTracking: the fetched rows are detached, so re-attach as Modified or
            // SaveChanges silently no-ops (see RegisterPushTokenCommandHandler).
            _context.PushTokens.Update(row);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Whether the email break-glass may be used at all.
    /// </summary>
    /// <remarks>
    /// Two switches, and both have to be on. The first says the owner wants the break-glass;
    /// the second says the owner knows what it costs and has written that down. In Production the
    /// configuration validator refuses to start when only the first is set, so the two cannot
    /// drift apart.
    /// </remarks>
    private bool BreakGlassIsOpen() =>
        _authOptions.Value.AdminOtpViaEmail
        && _authOptions.Value.AdminOtpViaEmailBreakGlassAcknowledged;

    /// <summary>
    /// Records that the weaker channel was used for a staff account. A break-glass that leaves no
    /// trace is not a break-glass, it is a second default.
    /// </summary>
    private async Task RecordBreakGlassUseAsync(User staff, CancellationToken cancellationToken)
    {
        _eventService.RecordEvent(
            "Auth",
            staff.Id.ToString(),
            "StaffOtpViaEmailBreakGlass",
            null,
            JsonSerializer.Serialize(new { staff.Id, staff.PhoneNumber, Role = staff.Role?.Name }),
            staff.Id,
            staff.PhoneNumber,
            $"The staff sign-in code for {staff.PhoneNumber} was delivered by email through the break-glass",
            staff.Id.ToString());

        // The audit row is committed on its own: nothing else changed.
        await _context.SaveChangesAsync(cancellationToken);
    }

    // Every Staff role, not just Admin: the break-glass serves a ProductOwner or Manager with an
    // email on file too. SeedRoles.StaffRoleNames is already the complete list, so there is
    // nothing to add here when a staff role is introduced.
    private static readonly string[] StaffRoleNames = SeedRoles.StaffRoleNames;

    /// <summary>The non-deleted Staff account bound to this phone, or null when no such account
    /// exists. The channel follows the account's current role and email at send time (§3/§15),
    /// so removing a staff member's email flips them back to SMS on the next request. IsActive is
    /// deliberately NOT filtered: an inactive staff member still authenticates by email. The code
    /// always goes to the address already on file for the phone, never to a caller-supplied
    /// one.</summary>
    private async Task<User?> TryResolveStaffAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        var staff = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u =>
                u.PhoneNumber == phoneNumber
                && !u.IsDeleted
                && u.Email != null && u.Email != ""
                && u.Role != null && StaffRoleNames.Contains(u.Role.Name),
                cancellationToken);

        return staff;
    }

    // Branded OTP email (HTML + retained plain-text fallback). The verification code is placed only
    // in the body/code block — never in the hidden preheader — so it does not leak into an inbox
    // notification preview. The plain-text alternative keeps the exact prior wording.
    internal static EmailMessage BuildAdminEmail(string code) =>
        BrandedEmailLayout.BuildMessage(
            AdminEmailSubject,
            BuildAdminEmailBody(code),
            new BrandedEmailLayout.Content
            {
                Preheader = "Your FuelFlow admin sign-in code is inside.",
                Heading = "Your sign-in code",
                Paragraphs = ["Use this code to finish signing in to FuelFlow."],
                Code = code,
                Footnote = "This code expires in 5 minutes. If you did not try to sign in, you can ignore this email.",
            });

    private static string BuildAdminEmailBody(string code) =>
        "Your FuelFlow admin verification code is " + code + ".\n\n" +
        "It expires in 5 minutes. If you did not try to sign in, ignore this email.";

    /// <summary>
    /// Resolution order: the dev-bypass constant (000000), then a fresh random code.
    /// </summary>
    private string ResolveCode()
    {
        if (_authOptions.Value.DevBypass)
            return "000000";

        return GenerateCode();
    }

    private static string GenerateCode()
    {
        // Cryptographically secure generation: Random.Shared is predictable,
        // which would let an attacker precompute upcoming codes. The range
        // excludes 000000 so a real code can never collide with the
        // dev-bypass code.
        return RandomNumberGenerator.GetInt32(1, 1_000_000).ToString("D6");
    }
}
