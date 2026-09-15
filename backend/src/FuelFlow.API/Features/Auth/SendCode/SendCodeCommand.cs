using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Notifications.Email;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
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

    private const string AdminEmailSubject = "FuelFlow admin sign-in code";

    public SendCodeCommandHandler(
        ApplicationDbContext context,
        ISmsService smsService,
        IPhoneNumberService phoneNumberService,
        ILogger<SendCodeCommandHandler> logger,
        IOptions<AuthOptions> authOptions,
        IEmailSender emailSender)
    {
        _context = context;
        _smsService = smsService;
        _phoneNumberService = phoneNumberService;
        _logger = logger;
        _authOptions = authOptions;
        _emailSender = emailSender;
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

        var code = ResolveCode();
        var verificationCode = new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber,
            Code = SecretsHasher.Hash(code),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            CreatedAtUtc = DateTime.UtcNow,
            IsUsed = false
        };

        _context.VerificationCodes.Add(verificationCode);
        await _context.SaveChangesAsync(cancellationToken);

        await DeliverCodeAsync(phoneNumber, code, cancellationToken);

        return new SendCodeResponse(true);
    }

    /// <summary>
    /// Routes the OTP to email for admin-role accounts (free) and to SMS for everyone
    /// else. Any gap in the email path - disabled, not configured, no email on file,
    /// or a send failure - falls back to SMS, so an admin is never locked out.
    /// </summary>
    private async Task DeliverCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        if (_authOptions.Value.AdminOtpViaEmail && _emailSender.IsConfigured)
        {
            var adminEmail = await TryResolveAdminEmailAsync(phoneNumber, cancellationToken);
            if (adminEmail is not null)
            {
                try
                {
                    await _emailSender.SendAsync(adminEmail, AdminEmailSubject, BuildAdminEmailBody(code), cancellationToken);
                    _logger.LogInformation("Admin verification code emailed to the account for {PhoneNumber}",
                        SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
                    return;
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                        throw;
                    _logger.LogWarning(ex,
                        "Admin OTP email failed for {PhoneNumber}; falling back to SMS",
                        SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
                }
            }
        }

        await _smsService.SendVerificationCodeAsync(phoneNumber, code, cancellationToken);
        _logger.LogInformation("Verification code sent to {PhoneNumber}",
            SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
    }

    /// <summary>Email of the active, non-deleted admin account bound to this phone, or null
    /// when no such admin exists. The code always goes to the address already on file for
    /// the phone, never to a caller-supplied one.</summary>
    private async Task<string?> TryResolveAdminEmailAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        var admin = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u =>
                u.PhoneNumber == phoneNumber
                && !u.IsDeleted
                && u.IsActive
                && u.Email != null && u.Email != ""
                && u.Role != null && u.Role.Name == AuthOptions.AdminRoleName,
                cancellationToken);

        return admin?.Email;
    }

    private static string BuildAdminEmailBody(string code) =>
        "Your FuelFlow admin verification code is " + code + ".\n\n" +
        "It expires in 10 minutes. If you did not try to sign in, ignore this email.";

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
