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

        // A Monobank test token creates invoices that settle in Monobank's sandbox and collect no
        // real money, while everything around it looks like a live payment system: orders are
        // created, vouchers are reserved, the reconciliation job reports them paid. So the failure
        // mode is not an error — it is a launch that looks successful and takes nothing. That is
        // only detectable if the state is written down, which is what this is for.
        //
        // Acknowledge-to-disable, like the two guards below: the sandbox phase is deliberate
        // today, and refusing to boot would stop work rather than make it safer. Remove the ack
        // together with the test token when #35 flips to real money.
        var monobankToken = configuration["Monobank:Token"] ?? "";
        if (monobankEnabled && monobankToken.StartsWith("test_", StringComparison.OrdinalIgnoreCase))
        {
            var acknowledged = configuration.GetValue<bool>("Monobank:AcknowledgeTestTokenInProduction");
            if (!acknowledged)
            {
                throw new InvalidOperationException(
                    "Refusing to start: Monobank is enabled with a TEST merchant token (Monobank:Token "
                    + "starts with 'test_'), so payments settle in Monobank's sandbox and no real money is "
                    + "ever collected - orders will be created and marked paid for nothing. Either set the "
                    + "live Monobank__Token, or set Monobank__AcknowledgeTestTokenInProduction=true to run "
                    + "on the sandbox as a deliberate, recorded decision.");
            }

            Log.Warning(
                "PAYMENTS: Production is running on a Monobank TEST merchant token, acknowledged explicitly. "
                + "No real money is being collected: invoices settle in Monobank's sandbox. This must be "
                + "replaced with the live token before taking real payments (#35).");
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

        // VoucherExpiration:Enabled is what makes fulfilment refuse expired stock, in both the
        // matcher and the atomic claim. When it is off, fulfilment happily hands the most-expired
        // stock it has to whoever buys first - which is exactly the stock a customer would least
        // want. It is read straight from configuration with no admin surface, so the only way to
        // reach it is an env var and a restart: a silent default nobody re-reads.
        //
        // Acknowledge-to-disable rather than a hard refusal, and deliberately keyed off
        // Monobank:Enabled rather than "is this Production": production is currently a Monobank
        // sandbox that still exercises expired stock on purpose, and a hard rule would block boot
        // today and halt that testing. What this changes is that the state has to be *stated* -
        // the same trade the reconciliation and DeviceAuth guards already make. When #35 introduces
        // a real-money signal, this should become a hard refusal with no ack accepted.
        var voucherExpirationEnabled = configuration.GetValue<bool?>("VoucherExpiration:Enabled") ?? true;
        if (monobankEnabled && !voucherExpirationEnabled)
        {
            var acknowledged = configuration.GetValue<bool>("VoucherExpiration:AcknowledgeDisabledInProduction");
            if (!acknowledged)
            {
                throw new InvalidOperationException(
                    "Refusing to start: Monobank is enabled but VoucherExpiration:Enabled is false "
                    + "in Production, so fulfilment hands already-expired vouchers to buyers and the "
                    + "checkout card is the only thing that flags it. Either set "
                    + "VoucherExpiration__Enabled=true, or set "
                    + "VoucherExpiration__AcknowledgeDisabledInProduction=true to sell expired stock as a "
                    + "deliberate, recorded decision.");
            }

            Log.Warning(
                "FULFILMENT: the voucher-expiration gate is disabled in Production by explicit "
                + "acknowledgement. Expired vouchers can be sold; customers receive fuel that is already "
                + "out of date. Clear this the moment real money is taken (see #35).");
        }
    }
}
