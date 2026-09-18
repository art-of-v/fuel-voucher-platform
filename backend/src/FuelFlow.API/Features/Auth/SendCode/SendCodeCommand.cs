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

    private const string StaffEmailSubject = "FuelFlow staff sign-in code";

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
    /// Routes the OTP to email for staff accounts (ProductOwner, Admin, Manager) and to SMS for regular users.
    /// Staff accounts must have email configured. If email is not configured or send fails, the error is logged
    /// and the code is NOT sent via SMS fallback - staff must have working email.
    /// </summary>
    private async Task DeliverCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u =>
                u.PhoneNumber == phoneNumber
                && !u.IsDeleted
                && u.IsActive,
                cancellationToken);

        var isStaff = user?.Role != null && SeedRoles.IsStaff(user.Role.Name);

        if (isStaff)
        {
            var email = user!.Email;
            if (string.IsNullOrEmpty(email))
            {
                _logger.LogWarning("Staff user {PhoneNumber} has no email configured; OTP not sent",
                    SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
                return;
            }

            if (!_emailSender.IsConfigured)
            {
                _logger.LogError("Email sender not configured; staff OTP not sent for {PhoneNumber}",
                    SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
                return;
            }

            try
            {
                await _emailSender.SendAsync(email, StaffEmailSubject, BuildStaffEmailBody(code), cancellationToken);
                _logger.LogInformation("Staff verification code emailed to {Email} for {PhoneNumber}",
                    SensitiveDataRedactor.MaskEmail(email), SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    throw;
                _logger.LogError(ex, "Staff OTP email failed for {PhoneNumber}; NO SMS fallback",
                    SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
            }
            return;
        }

        await _smsService.SendVerificationCodeAsync(phoneNumber, code, cancellationToken);
        _logger.LogInformation("Verification code sent to {PhoneNumber}",
            SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
    }

    private static string BuildStaffEmailBody(string code) =>
        "Your FuelFlow staff verification code is " + code + ".\n\n" +
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
