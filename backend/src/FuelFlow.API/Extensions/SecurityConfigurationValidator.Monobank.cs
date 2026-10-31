using FuelFlow.SharedKernel.Security;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace FuelFlow.API.Extensions;

internal static partial class SecurityConfigurationValidator
{
    /// <summary>
    /// The Monobank settings: the public key every webhook is verified with, the merchant token,
    /// and the reconciliation backstop for a webhook that never arrives.
    /// </summary>
    /// <returns>Whether Monobank is enabled, which the other families key their rules off.</returns>
    private static bool ValidateMonobank(IConfiguration configuration)
    {
        var monobankEnabled = configuration.GetValue<bool?>("Monobank:Enabled") ?? false;
        var monobankPublicKey = configuration["Monobank:PublicKey"] ?? "";

        if (monobankEnabled &&
            (string.IsNullOrWhiteSpace(monobankPublicKey)
             || monobankPublicKey.Contains("PRODUCTION_PUBLIC_KEY_HERE", StringComparison.OrdinalIgnoreCase)
             || monobankPublicKey.Contains("YOUR_MONOBANK_PUBLIC_KEY", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Refusing to start: Monobank is enabled but Monobank:PublicKey is not configured. "
                + "Webhook signature verification cannot be enforced without the real Monobank public key.");
        }

        // "Is it set" is not "is it usable". This one cost a production incident: Monobank:PublicKey
        // held 100 characters of a 240-character key, because an env file was hand-edited and the
        // value was truncated somewhere along the way. Every existing check passed - it was set, and
        // it was not a placeholder - and then every webhook failed inside signature verification, so
        // no payment was ever confirmed by its own callback. Reconciliation silently became the only
        // thing completing orders. A configured-looking key that cannot be read is caught here,
        // where the fix is "fix the env var", instead of in production, where the symptom is a
        // paying customer with no voucher.
        if (monobankEnabled && !string.IsNullOrWhiteSpace(monobankPublicKey)
            && !PublicKeyFormat.IsReadable(monobankPublicKey))
        {
            throw new InvalidOperationException(
                "Refusing to start: Monobank:PublicKey is set but is not a readable public key "
                + $"(it is {monobankPublicKey.Trim().Length} characters; a Monobank key is 240). This is "
                + "almost always a value truncated while editing the env file. Fetch the key with "
                + "`curl -H 'X-Token: $MONOBANK_TOKEN' https://api.monobank.ua/api/merchant/pubkey`, take the "
                + "\"key\" field, and put the whole value in MONOBANK_PUBLIC_KEY. Until it is fixed, every "
                + "Monobank webhook is rejected and reconciliation is the only thing completing payments.");
        }

        // A Monobank test token creates invoices that settle in Monobank's sandbox and collect no
        // real money, while everything around it looks like a live payment system: orders are
        // created, vouchers are reserved, the reconciliation job reports them paid. So the failure
        // mode is not an error — it is a launch that looks successful and takes nothing.
        //
        // This is a hard refusal, not acknowledge-to-disable. It used to be an acknowledgement
        // because the sandbox phase was deliberate; that phase ended when production moved to the
        // live merchant on 2026-10-11 (planning #192, #221, #679), so there is no legitimate state
        // to acknowledge any more. The acknowledgement setting is deliberately gone rather than
        // ignored: a leftover in a deploy env file must not be able to turn this back into a
        // warning, and a setting nobody reads is a setting that lies about its own effect.
        var monobankToken = configuration["Monobank:Token"] ?? "";
        if (monobankEnabled && monobankToken.StartsWith("test_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to start: Monobank is enabled with a TEST merchant token (Monobank:Token "
                + "starts with 'test_'), so payments settle in Monobank's sandbox and no real money is "
                + "ever collected - orders will be created and marked paid for nothing. Set the live "
                + "Monobank__Token. Production has taken real money since 2026-10-11, so a sandbox token "
                + "is never a valid configuration here and no acknowledgement makes it one.");
        }

        // MonobankReconciliationService is the only backstop for a payment webhook that never
        // arrives: it polls invoice status for still-unpaid orders and drives a paid-but-unnotified
        // one through the same fulfilment path a webhook would. ReconciliationEnabled defaults to
        // true, but a stray config flag can turn it off - and then a single lost webhook is a
        // customer charged with no voucher and nothing to catch it. Like the DeviceAuth guard below
        // this is acknowledge-to-disable, not a hard refusal: there can be a real reason to pause
        // reconciliation, but it must be a deliberate, recorded choice rather than a silent default.
        var reconciliationEnabled = configuration.GetValue<bool?>("Monobank:ReconciliationEnabled") ?? true;
        if (monobankEnabled && !reconciliationEnabled)
        {
            var acknowledged = configuration.GetValue<bool>("Monobank:AcknowledgeReconciliationDisabled");
            if (!acknowledged)
            {
                throw new InvalidOperationException(
                    "Refusing to start: Monobank is enabled but Monobank:ReconciliationEnabled is false "
                    + "in Production, so lost or late payment webhooks are never reconciled - a paid order "
                    + "can be left unfulfilled with no retry. Either set Monobank__ReconciliationEnabled=true, "
                    + "or set Monobank__AcknowledgeReconciliationDisabled=true to run without the "
                    + "reconciliation safety net as a deliberate, recorded decision.");
            }

            Log.Warning(
                "PAYMENTS: Monobank reconciliation is disabled in Production by explicit acknowledgement. "
                + "Lost or late payment webhooks will not be recovered; a paid order can remain unfulfilled.");
        }

        return monobankEnabled;
    }
}
