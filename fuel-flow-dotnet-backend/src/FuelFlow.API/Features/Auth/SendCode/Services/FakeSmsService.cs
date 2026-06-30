using FuelFlow.Features.Auth.SendCode.Abstractions;

namespace FuelFlow.Features.Auth.SendCode.Services;

public sealed class FakeSmsService : ISmsService
{
    private readonly ILogger<FakeSmsService> _logger;

    public FakeSmsService(ILogger<FakeSmsService> logger)
    {
        _logger = logger;
    }

    public Task SendVerificationCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        _logger.LogWarning("DEVELOPMENT MODE: Fake SMS sent to {PhoneNumber}. Verification code: {Code}", phoneNumber, code);
        _logger.LogWarning("Use code '000000' for testing in development");

        return Task.CompletedTask;
    }
}
