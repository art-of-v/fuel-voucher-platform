using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace FuelFlow.API.Extensions;

/// <summary>
/// Production-only "refuse to boot" guards for configuration that is unsafe by omission: a stray
/// flag or a missing secret that would silently disable a security or payment-integrity control.
/// Extracted from Program.cs so the individual rules are unit-testable; invoked once at startup.
/// </summary>
internal static class SecurityConfigurationValidator
{
    internal static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsProduction())
            return;

        var monobankEnabled = configuration.GetValue<bool?>("Monobank:Enabled") ?? false;
        var monobankPublicKey = configuration["Monobank:PublicKey"] ?? "";

        if (monobankEnabled &&
            (string.IsNullOrWhiteSpace(monobankPublicKey)
             || monobankPublicKey.Contains("PRODUCTION_PUBLIC_KEY_HERE", StringComparison.OrdinalIgnoreCase)
             || monobankPublicKey.Contains("YOUR_MONOBANK_PUBLIC_KEY", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Refusing to start: Monobank is enabled but Monobank:PublicKey is not configured. " +
                "Webhook signature verification cannot be enforced without the real Monobank public key.");
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

        // Auth:DevBypass is a single switch that turns off most of the auth surface at once:
        // ISmsService becomes FakeSmsService (OTP codes only ever reach the log stream, so anyone
        // who can read logs can log in as anyone), every OTP and global rate limit becomes
        // int.MaxValue, and the Hangfire dashboard drops its authorization filter. One stray
        // environment variable in the deploy config is therefore a full authentication bypass plus
        // an unauthenticated job-management console. It must never be set in Production.
        var devBypass = configuration.GetValue<bool>("Auth:DevBypass");
        if (devBypass)
        {
            throw new InvalidOperationException(
                "Refusing to start: Auth:DevBypass is enabled in Production. This disables real SMS "
                + "delivery (OTP codes go to logs only), removes every rate limit, and unauthenticates "
                + "the Hangfire dashboard. Unset Auth__DevBypass.");
        }

        // OTP codes must actually reach users' phones. With DevBypass off and no
        // SMS provider credentials the app silently falls back to FakeSmsService: codes
        // are only written to logs, nobody can log in, and the failure is easy to
        // miss. Refuse to start instead, like the Monobank guard above.
        var hasSmsClub = ServiceSetup.HasSmsClubConfiguration(configuration);
        if (!hasSmsClub)
        {
            throw new InvalidOperationException(
                "Refusing to start: Auth:DevBypass is off but SMS Club is not configured. " +
                "Set SmsClub__Token and SmsClub__SenderName — " +
                "OTP codes would never be delivered (silent FakeSmsService fallback).");
        }

        // DeviceAuth:Enabled defaults to false, and when it is false DeviceSignatureMiddleware
        // returns before it checks anything at all - device binding on /api/purchases silently
        // does not exist. Shipping that by omission is the failure mode worth blocking.
        //
        // This is NOT a hard refusal, because enabling signature enforcement is coupled to the
        // released mobile build: turning it on before a signing client is in users' hands locks
        // them out of checkout. So the operator has to state the choice in config rather than
        // arrive at it by default.
        var deviceAuthEnabled = configuration.GetValue<bool>("DeviceAuth:Enabled");
        if (!deviceAuthEnabled)
        {
            var acknowledged = configuration.GetValue<bool>("DeviceAuth:AcknowledgeDisabledInProduction");
            if (!acknowledged)
            {
                throw new InvalidOperationException(
                    "Refusing to start: DeviceAuth:Enabled is false in Production, so device signature "
                    + "verification on the checkout endpoints is inactive. Either set DeviceAuth__Enabled=true "
                    + "(only once a signing mobile build is released - enabling it earlier breaks checkout for "
                    + "existing installs), or set DeviceAuth__AcknowledgeDisabledInProduction=true to run "
                    + "without device binding as a deliberate, recorded decision.");
            }

            Log.Warning(
                "SECURITY: DeviceAuth is disabled in Production by explicit acknowledgement. "
                + "Checkout requests are not device-bound; a stolen access token is sufficient to purchase.");
        }
    }
}
