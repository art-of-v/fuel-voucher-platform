using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace FuelFlow.Features.Auth.SendCode.Services;

public sealed class TwilioSmsService : ISmsService
{
    private readonly TwilioOptions _options;
    private readonly SmsBudgetGuard _budget;
    private readonly ILogger<TwilioSmsService> _logger;

    public TwilioSmsService(
        IOptions<TwilioOptions> options,
        SmsBudgetGuard budget,
        ILogger<TwilioSmsService> logger)
    {
        _options = options.Value;
        _budget = budget;
        _logger = logger;
        TwilioClient.Init(_options.AccountSid, _options.AuthToken);
    }

    public async Task SendVerificationCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        // Spend ceiling first: per-phone and per-IP limits cannot see an attacker cycling
        // thousands of distinct numbers, and every send past this point costs real money.
        if (!_budget.TryConsume())
            throw new SmsBudgetExhaustedException();

        await SendWithoutBudgetCheckAsync(phoneNumber, code, cancellationToken);
    }

    /// <summary>
    /// Sends without consuming the shared daily budget. Used by <see cref="SmsClubSmsService"/>
    /// as a runtime fallback: the budget was already consumed once for the whole attempt, so a
    /// provider retry must not be double-charged against the ceiling.
    /// </summary>
    internal async Task SendWithoutBudgetCheckAsync(string phoneNumber, string code, CancellationToken cancellationToken)
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

/// <summary>
/// Thrown when the daily outbound SMS budget is spent. Distinct from a transport failure so the
/// caller can answer 503 rather than retrying into an empty budget.
/// </summary>
public sealed class SmsBudgetExhaustedException : Exception
{
    public SmsBudgetExhaustedException()
        : base("Outbound SMS budget exhausted")
    {
    }
}
