using Microsoft.Extensions.Configuration;
using Serilog;

namespace FuelFlow.API.Extensions;

internal static partial class SecurityConfigurationValidator
{
    /// <summary>
    /// The switches that decide how a sign-in proves who somebody is: the development bypass, the
    /// staff second factor, the email break-glass, and the SMS delivery without which a code
    /// reaches nobody.
    /// </summary>
    private static void ValidateAuthentication(IConfiguration configuration)
    {
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

        // A staff sign-in has to prove a second factor. The staff tier can move money, and a
        // single factor that arrives in a mailbox is not a control: the mailbox is the channel an
        // attacker can influence and the one a phishing campaign reaches. The switch exists for
        // the local development loop and for a rollback - not as a way to run the platform, so
        // Production refuses to start without it. Like DevBypass above, this is a hard refusal:
        // there is no legitimate reason for the production box to be configured this way, and the
        // cost of being wrong is an account that can move money.
        var staffSecondFactorRequired = configuration.GetValue<bool?>("Auth:StaffSecondFactorRequired") ?? true;
        if (!staffSecondFactorRequired)
        {
            throw new InvalidOperationException(
                "Refusing to start: Auth:StaffSecondFactorRequired is false in Production, so a staff "
                + "sign-in completes with a single factor - a code that arrives in a mailbox, for an "
                + "account that can move money. Unset Auth__StaffSecondFactorRequired (the default is true).");
        }

        // The email break-glass. Two switches, and the second one is the point: the owner has to
        // say in writing that Production runs with the weaker channel. Without the acknowledgement
        // the setting could be flipped for an afternoon and stay flipped, and nobody would know
        // that the staff codes went to a mailbox instead of to the SIM.
        var adminOtpViaEmail = configuration.GetValue<bool>("Auth:AdminOtpViaEmail");
        var breakGlassAcknowledged = configuration.GetValue<bool>("Auth:AdminOtpViaEmailBreakGlassAcknowledged");
        if (adminOtpViaEmail && !breakGlassAcknowledged)
        {
            throw new InvalidOperationException(
                "Refusing to start: Auth:AdminOtpViaEmail is true in Production without "
                + "Auth:AdminOtpViaEmailBreakGlassAcknowledged. The email channel is a break-glass for the "
                + "day the SMS gateway is down, and it delivers the staff sign-in code to a mailbox. Either set "
                + "Auth__AdminOtpViaEmail=false, or set Auth__AdminOtpViaEmailBreakGlassAcknowledged=true to run "
                + "on the weaker channel as a deliberate, recorded decision.");
        }

        if (adminOtpViaEmail)
        {
            Log.Warning(
                "SECURITY: Production delivers the staff sign-in code by email, acknowledged explicitly. "
                + "Every use is recorded as a security event. Clear this as soon as the SMS gateway is back.");
        }

        // OTP codes must actually reach users' phones. With DevBypass off and no
        // SMS brand credentials the app silently falls back to FakeSmsService: codes
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
    }
}
