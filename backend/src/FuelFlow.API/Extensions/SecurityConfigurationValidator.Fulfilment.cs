using Microsoft.Extensions.Configuration;
using Serilog;

namespace FuelFlow.API.Extensions;

internal static partial class SecurityConfigurationValidator
{
    /// <summary>
    /// The voucher-expiration gate: what makes fulfilment refuse stock that has gone out of date.
    /// </summary>
    private static void ValidateFulfilment(IConfiguration configuration, bool monobankEnabled)
    {
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
