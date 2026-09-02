using System.Threading.RateLimiting;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Auth.SendCode.Services;

/// <summary>
/// Hard ceiling on outbound SMS per rolling 24 hours.
/// <para>
/// Per-phone and per-IP rate limits bound how fast any one actor can pump OTPs, but they do not
/// bound the total spend: an attacker cycling thousands of distinct phone numbers stays inside
/// every per-key limit while running the Twilio balance to zero, which also denies OTP delivery
/// to real users. This guard is the spend backstop and it fails closed.
/// </para>
/// <para>
/// Deliberately in-process, which for the current single-host deployment is exact and lock-free.
/// If the API is ever scaled to more than one instance the ceiling becomes per-instance and this
/// needs to move to a shared counter.
/// </para>
/// </summary>
public sealed class SmsBudgetGuard : IDisposable
{
    private readonly FixedWindowRateLimiter _limiter;
    private readonly ILogger<SmsBudgetGuard> _logger;
    private readonly int _dailyLimit;

    public SmsBudgetGuard(IOptions<SmsOptions> options, ILogger<SmsBudgetGuard> logger)
    {
        _logger = logger;
        _dailyLimit = options.Value.DailySendLimit > 0
            ? options.Value.DailySendLimit
            : SmsOptions.DefaultDailySendLimit;

        _limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = _dailyLimit,
            Window = TimeSpan.FromHours(24),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    /// <summary>
    /// Consumes one unit of the daily budget. Returns false when the budget is exhausted,
    /// in which case the caller must not send.
    /// </summary>
    public bool TryConsume()
    {
        var lease = _limiter.AttemptAcquire(permitCount: 1);

        if (lease.IsAcquired)
            return true;

        // Loud on purpose: hitting this means either an attack or a misconfigured client, and
        // in both cases OTP delivery is now degraded for everyone.
        _logger.LogError(
            "Outbound SMS budget of {DailyLimit} per 24h is exhausted; refusing to send. "
            + "This is either an OTP pump or a client retry loop - investigate before raising the limit.",
            _dailyLimit);

        return false;
    }

    public void Dispose() => _limiter.Dispose();
}
