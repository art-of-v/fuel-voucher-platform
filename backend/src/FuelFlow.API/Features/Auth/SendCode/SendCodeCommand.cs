using FuelFlow.Features.Auth.QaTestAccess;
using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Notifications.Email;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace FuelFlow.Features.Auth.SendCode;

public sealed record SendCodeCommand(string PhoneNumber);

public sealed record SendCodeResponse(bool Success);

public sealed class SendCodeCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ISmsService _smsService;
    private readonly IPhoneNumberService _phoneNumberService;
    private readonly ILogger<SendCodeCommandHandler> _logger;
    private readonly IOptions<AuthOptions> _authOptions;
    private readonly IEmailSender _emailSender;
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
        QaTestAccessService? qaTestAccess = null,
        FuelFlowMetrics? metrics = null)
    {
        _context = context;
        _smsService = smsService;
        _phoneNumberService = phoneNumberService;
        _logger = logger;
        _authOptions = authOptions;
        _emailSender = emailSender;
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
        // never held anywhere) and skip SMS entirely — no provider cost, works before the gateway
        // is live, and the fixed code never rides an SMS. Any other phone, or QA disabled, falls
        // through to the ordinary random-code path below, so the QA number is indistinguishable
        // from any other number when the feature is off (no enumeration oracle, spec §7).
        if (_qaTestAccess is not null
            && _qaTestAccess.IsQaPhone(phoneNumber)
            && await _qaTestAccess.IsActiveAsync(cancellationToken))
        {
            _context.VerificationCodes.Add(new VerificationCode
            {
                Id = Guid.NewGuid(),
                PhoneNumber = phoneNumber,
                Code = _qaTestAccess.CodeHash!, // already a SHA-256 hash of the configured QA code
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
                CreatedAtUtc = DateTime.UtcNow,
                IsUsed = false
            });
            await _context.SaveChangesAsync(cancellationToken);

            // No code, ever, in the log line.
            _logger.LogInformation("QA test-access code issued for the QA account; no SMS sent");
            _metrics?.QaTestAccessSendCode();

            return new SendCodeResponse(true);
        }

        var code = ResolveCode();
        var verificationCode = new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber,
            Code = SecretsHasher.Hash(code),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5), // spec §2: verification codes valid for 5 minutes
            CreatedAtUtc = DateTime.UtcNow,
            IsUsed = false
        };

        _context.VerificationCodes.Add(verificationCode);
        await _context.SaveChangesAsync(cancellationToken);

        await DeliverCodeAsync(phoneNumber, code, cancellationToken);

        return new SendCodeResponse(true);
    }

    /// <summary>
    /// Routes the OTP to email for any Staff account with an email on file (free) and to
    /// SMS for everyone else (spec §3/§15 — the channel follows the account's current
    /// email, not a hard-coded role). Any gap in the email path - disabled, not
    /// configured, no email on file, or a send failure - falls back to SMS, so a staff
    /// member is never locked out and a brand-new or not-yet-activated user always
    /// receives their code. IsActive gates only voucher purchase, never OTP delivery, so
    /// it is not consulted here.
    /// </summary>
    private async Task DeliverCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        if (_authOptions.Value.AdminOtpViaEmail && _emailSender.IsConfigured)
        {
            var staffEmail = await TryResolveStaffEmailAsync(phoneNumber, cancellationToken);
            if (staffEmail is not null)
            {
                try
                {
                    await _emailSender.SendAsync(staffEmail, BuildAdminEmail(code), cancellationToken);
                    _logger.LogInformation("Staff verification code emailed to the account for {PhoneNumber}",
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
        }

        await _smsService.SendVerificationCodeAsync(phoneNumber, code, cancellationToken);
        _logger.LogInformation("Verification code sent to {PhoneNumber}",
            SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
    }

    // Every Staff role, not just Admin: a ProductOwner or Manager with an email on file
    // authenticates by email too (spec §3/§15). New Staff roles added later must be added here.
    private static readonly string[] StaffRoleNames =
        { SeedRoles.ProductOwnerName, SeedRoles.AdminName, SeedRoles.ManagerName };

    /// <summary>Email of the non-deleted Staff account bound to this phone, or null when no
    /// such account exists. The channel follows the account's current role and email at send
    /// time (§3/§15), so removing a staff member's email flips them back to SMS on the next
    /// request. IsActive is deliberately NOT filtered: an inactive staff member still
    /// authenticates by email. The code always goes to the address already on file for the
    /// phone, never to a caller-supplied one.</summary>
    private async Task<string?> TryResolveStaffEmailAsync(string phoneNumber, CancellationToken cancellationToken)
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

        return staff?.Email;
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
