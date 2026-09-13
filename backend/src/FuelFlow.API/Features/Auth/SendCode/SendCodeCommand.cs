using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.SharedKernel.Observability;
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

    public SendCodeCommandHandler(
        ApplicationDbContext context,
        ISmsService smsService,
        IPhoneNumberService phoneNumberService,
        ILogger<SendCodeCommandHandler> logger,
        IOptions<AuthOptions> authOptions)
    {
        _context = context;
        _smsService = smsService;
        _phoneNumberService = phoneNumberService;
        _logger = logger;
        _authOptions = authOptions;
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

        var code = ResolveCode(phoneNumber, out var isTestPhone);
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

        if (isTestPhone)
        {
            // Allowlisted QA numbers get their fixed code without any SMS:
            // no provider cost, and it works before the SMS gateway is live.
            _logger.LogWarning(
                "TEST PHONE: OTP issued for allowlisted test number {PhoneNumber}; no SMS sent",
                SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
        }
        else
        {
            await _smsService.SendVerificationCodeAsync(phoneNumber, code, cancellationToken);
            _logger.LogInformation("Verification code sent to {PhoneNumber}",
                SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
        }

        return new SendCodeResponse(true);
    }

    /// <summary>
    /// Resolution order: the dev-bypass constant (000000), then the fixed
    /// code of an allowlisted test phone, then a fresh random code.
    /// </summary>
    private string ResolveCode(string phoneNumber, out bool isTestPhone)
    {
        isTestPhone = false;

        if (_authOptions.Value.DevBypass)
            return "000000";

        if (_authOptions.Value.TestPhones.TryGetValue(phoneNumber, out var testCode))
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(testCode, @"^\d{6}$"))
            {
                isTestPhone = true;
                return testCode;
            }

            _logger.LogError(
                "Test phone {PhoneNumber} has a malformed configured code; treating it as a regular phone",
                SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
        }

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
