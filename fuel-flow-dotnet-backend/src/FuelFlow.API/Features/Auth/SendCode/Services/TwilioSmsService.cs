using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.Options;
using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace FuelFlow.Features.Auth.SendCode.Services;

public sealed class TwilioSmsService : ISmsService
{
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsService> _logger;

    public TwilioSmsService(IOptions<TwilioOptions> options, ILogger<TwilioSmsService> logger)
    {
        _options = options.Value;
        _logger = logger;
        TwilioClient.Init(_options.AccountSid, _options.AuthToken);
    }

    public async Task SendVerificationCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        try
        {
            var message = await MessageResource.CreateAsync(
                body: $"Your FuelFlow verification code is: {code}",
                from: new PhoneNumber(_options.PhoneNumber),
                to: new PhoneNumber(phoneNumber)
            );

            _logger.LogInformation("SMS sent successfully to {PhoneNumber}. SID: {MessageSid}", phoneNumber, message.Sid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {PhoneNumber}", phoneNumber);
            throw;
        }
    }
}
