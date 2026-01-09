using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace FuelFlow.Api.Infrastructure.Services;

public interface ITwilioService
{
    Task<bool> SendVerificationCodeAsync(string phoneNumber, string code);
}

public class TwilioService : ITwilioService
{
    private readonly string? _accountSid;
    private readonly string? _authToken;
    private readonly string? _phoneNumber;
    private readonly bool _isConfigured;

    public TwilioService(IConfiguration configuration)
    {
        _accountSid = configuration["Twilio:AccountSid"] 
            ?? Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID");
        
        _authToken = configuration["Twilio:AuthToken"] 
            ?? Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN");
        
        _phoneNumber = configuration["Twilio:PhoneNumber"] 
            ?? Environment.GetEnvironmentVariable("TWILIO_PHONE_NUMBER");

        _isConfigured = !string.IsNullOrEmpty(_accountSid) && !string.IsNullOrEmpty(_authToken) && !string.IsNullOrEmpty(_phoneNumber);

        if (_isConfigured)
        {
            TwilioClient.Init(_accountSid, _authToken);
        }
    }

    public async Task<bool> SendVerificationCodeAsync(string phoneNumber, string code)
    {
        if (!_isConfigured)
        {
            Console.WriteLine($"Twilio not configured. Would send code {code} to {phoneNumber}");
            return true;
        }

        try
        {
            var message = await MessageResource.CreateAsync(
                body: $"Your Fuel Flow verification code is: {code}",
                from: new PhoneNumber(_phoneNumber!),
                to: new PhoneNumber(phoneNumber)
            );

            return message.ErrorCode == null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Twilio error: {ex.Message}");
            return false;
        }
    }
}
