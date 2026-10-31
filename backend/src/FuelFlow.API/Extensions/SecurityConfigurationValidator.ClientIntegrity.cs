using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace FuelFlow.API.Extensions;

internal static partial class SecurityConfigurationValidator
{
    /// <summary>
    /// The two client-integrity controls: device-bound checkout requests and app attestation. Both
    /// default to off, and both are acknowledge-to-disable rather than a hard refusal, because
    /// enforcing them before a build that can produce a proof is live locks existing installs out.
    /// </summary>
    private static void ValidateClientIntegrity(IConfiguration configuration)
    {
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

        // Attestation is the client-integrity control on auth and checkout. Its safe-looking
        // default is Enabled=false: the middleware then returns before it checks anything, and
        // the endpoint names in ProtectedEndpoints read as protected while nothing is verified.
        // Like DeviceAuth, enforcing before a build that can produce a proof is live would lock
        // existing installs out, so this is acknowledge-to-disable rather than a hard refusal.
        // The acknowledgement is what makes the state a decision instead of an omission.
        var attestationEnabled = configuration.GetValue<bool>("Attestation:Enabled");
        var attestationModeValue = configuration["Attestation:Mode"];

        if (!Enum.TryParse<AttestationMode>(attestationModeValue ?? "ReportOnly", ignoreCase: true, out var attestationMode))
        {
            throw new InvalidOperationException(
                $"Refusing to start: Attestation:Mode '{attestationModeValue}' is not a known mode. "
                + "Use Off, ReportOnly or Enforce.");
        }

        var attestationEnforcing = attestationEnabled && attestationMode == AttestationMode.Enforce;

        if (!attestationEnforcing)
        {
            var acknowledged = configuration.GetValue<bool>("Attestation:AcknowledgeDisabledInProduction");
            if (!acknowledged)
            {
                throw new InvalidOperationException(
                    "Refusing to start: app attestation is not enforcing in Production "
                    + $"(Attestation:Enabled={attestationEnabled}, Mode={attestationMode}). "
                    + "Either enable it and set Mode=Enforce once an attesting mobile build is live, "
                    + "or set Attestation__AcknowledgeDisabledInProduction=true to run without the "
                    + "client-integrity control as a deliberate, recorded decision.");
            }

            Log.Warning(
                "SECURITY: app attestation is not enforcing in Production by explicit acknowledgement "
                + $"(Enabled={attestationEnabled}, Mode={attestationMode}). Auth and checkout requests "
                + "carry no verified client-integrity proof.");
        }
        else
        {
            var platforms = configuration
                .GetSection("Attestation:EnforcePlatforms")
                .Get<string[]>() ?? [];

            if (platforms.Length == 0)
            {
                throw new InvalidOperationException(
                    "Refusing to start: Attestation:Mode is Enforce but Attestation:EnforcePlatforms "
                    + "is empty, so no request can ever be enforced. Name at least one platform (ios, "
                    + "android) or set Mode=ReportOnly.");
            }

            if (platforms.Any(platform => platform.Equals("ios", StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrWhiteSpace(configuration["Attestation:AppleAppId"]))
            {
                throw new InvalidOperationException(
                    "Refusing to start: iOS attestation is enforcing but Attestation:AppleAppId is "
                    + "not set. App Attest verification needs the team id and bundle id "
                    + "(TB929MCW58.com.fuelflowmarketplace.www).");
            }

            if (platforms.Any(platform => platform.Equals("android", StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrWhiteSpace(configuration["Attestation:GoogleCredentialsJson"]))
            {
                throw new InvalidOperationException(
                    "Refusing to start: Android attestation is enforcing but "
                    + "Attestation:GoogleCredentialsJson is not set. Play Integrity verification "
                    + "needs the service-account JSON from Attestation__GoogleCredentialsJson.");
            }
        }
    }
}
