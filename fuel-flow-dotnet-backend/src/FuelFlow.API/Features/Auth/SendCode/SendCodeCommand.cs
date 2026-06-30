using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.Features.Auth.SharedAbstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.SendCode;

public sealed record SendCodeCommand(string PhoneNumber);

public sealed record SendCodeResponse(bool Success);

public sealed class SendCodeCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ISmsService _smsService;
    private readonly IPhoneNumberService _phoneNumberService;
    private readonly ILogger<SendCodeCommandHandler> _logger;
    private readonly IHostEnvironment _environment;

    public SendCodeCommandHandler(
        ApplicationDbContext context,
        ISmsService smsService,
        IPhoneNumberService phoneNumberService,
        ILogger<SendCodeCommandHandler> logger,
        IHostEnvironment environment)
    {
        _context = context;
        _smsService = smsService;
        _phoneNumberService = phoneNumberService;
        _logger = logger;
        _environment = environment;
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

        var code = _environment.IsDevelopment() ? "000000" : GenerateCode();
        var verificationCode = new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber,
            Code = code,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            CreatedAtUtc = DateTime.UtcNow,
            IsUsed = false
        };

        _context.VerificationCodes.Add(verificationCode);
        await _context.SaveChangesAsync(cancellationToken);

        await _smsService.SendVerificationCodeAsync(phoneNumber, code, cancellationToken);

        _logger.LogInformation("Verification code sent to {PhoneNumber}", phoneNumber);

        return new SendCodeResponse(true);
    }

    private static string GenerateCode()
    {
        return Random.Shared.Next(100000, 999999).ToString();
    }
}
