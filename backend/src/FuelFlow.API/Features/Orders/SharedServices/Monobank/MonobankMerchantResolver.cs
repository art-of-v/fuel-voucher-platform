using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.API.Features.Orders.SharedServices.Monobank;

/// <summary>
/// Decides which Monobank merchant a given account is allowed to spend.
/// </summary>
public interface IMonobankMerchantResolver
{
    /// <summary>
    /// The merchant this account must transact against.
    /// </summary>
    /// <exception cref="MonobankMerchantUnavailableException">
    /// The account routes to a merchant that is not configured. Raised rather than downgraded to
    /// the other merchant - see the remarks on <see cref="Resolve"/>.
    /// </exception>
    MonobankMerchant Resolve(string? phoneNumber, bool isQaAccount);
}

/// <summary>
/// Routes QA accounts to the sandbox merchant and everyone else to the live one.
/// </summary>
/// <remarks>
/// The rule is one-directional on purpose. A QA account must have no path to the live token:
/// that is the only direction in which testing can take a real customer's money.
///
/// The opposite hazard is handled by failing closed rather than by falling back. If a QA
/// account resolves to the sandbox and the sandbox is not configured, the checkout is refused.
/// Falling back to live would take real money from someone who is testing, and - worse - would
/// look like a successful payment, so nothing would report it.
/// </remarks>
public sealed class MonobankMerchantResolver : IMonobankMerchantResolver
{
    private readonly MonobankOptions _options;
    private readonly ILogger<MonobankMerchantResolver> _logger;

    public MonobankMerchantResolver(
        IOptions<MonobankOptions> options,
        ILogger<MonobankMerchantResolver> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public MonobankMerchant Resolve(string? phoneNumber, bool isQaAccount)
    {
        if (!IsQa(phoneNumber, isQaAccount))
        {
            return MonobankMerchant.Live;
        }

        // Refuse rather than default. An unconfigured sandbox must never become a live charge.
        if (!_options.IsConfigured(MonobankMerchant.Sandbox))
        {
            _logger.LogError(
                "Refusing checkout: the account is a QA account but Monobank:SandboxToken is not configured. " +
                    "Falling back to the live merchant would take real money from a test account.");
            throw new MonobankMerchantUnavailableException(MonobankMerchant.Sandbox);
        }

        _logger.LogInformation(
            "QA account routes to the Monobank sandbox merchant (allowlist match: {Allowlisted})",
            !string.IsNullOrWhiteSpace(phoneNumber) && _options.QaPhones.Contains(phoneNumber, StringComparer.Ordinal));
        return MonobankMerchant.Sandbox;
    }

    private bool IsQa(string? phoneNumber, bool isQaAccount)
    {
        // Either server-side signal routes to the sandbox. The allowlist (deploy config) is
        // the primary authority; the profile flag (admin-set in the DB, the same signal that
        // routes test stock) is honoured too, so a flagged QA account can never pay live money
        // just because someone forgot its phone in Monobank:QaPhones. Neither is self-service,
        // which is the property that makes this safe - see the remarks on MonobankOptions.QaPhones.
        var allowlisted = !string.IsNullOrWhiteSpace(phoneNumber)
            && _options.QaPhones.Contains(phoneNumber, StringComparer.Ordinal);
        return allowlisted || isQaAccount;
    }
}

/// <summary>
/// The merchant a checkout was routed to is not usable. Raised instead of silently using a
/// different merchant.
/// </summary>
public sealed class MonobankMerchantUnavailableException : Exception
{
    public const string Code = "monobank_merchant_unavailable";

    public MonobankMerchantUnavailableException(MonobankMerchant merchant)
        : base($"Monobank merchant '{merchant}' is not configured; refusing to route this payment elsewhere")
    {
        Merchant = merchant;
    }

    public MonobankMerchant Merchant { get; }
}