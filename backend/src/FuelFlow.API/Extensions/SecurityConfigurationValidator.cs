using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FuelFlow.API.Extensions;

/// <summary>
/// Production-only "refuse to boot" guards for configuration that is unsafe by omission: a stray
/// flag or a missing secret that would silently disable a security or payment-integrity control.
/// Extracted from Program.cs so the individual rules are unit-testable; invoked once at startup.
/// </summary>
/// <remarks>
/// The rules live one file per family - payments, authentication, client integrity, fulfilment,
/// database transport and placeholder secrets - because each family is an independent set of
/// checks with its own reasons, and they were one 536-line file. The order of the calls is the
/// order the checks used to run in: the first refusal a misconfigured host meets is the one the
/// operator sees, so moving a family up or down changes the message a test or a person gets.
/// </remarks>
internal static partial class SecurityConfigurationValidator
{
    internal static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        // Development is exempt from everything here: a developer's box runs on generated keys and
        // a fake SMS provider, and a guard that stopped it from starting would be a guard that
        // would be turned off.
        if (environment.IsDevelopment())
            return;

        // The placeholder check is NOT production-only. Staging is a second, fully isolated stack
        // on the same host, behind an HTTP Basic credential - and it is deployed from a template
        // that ships the same placeholders as production's. A placeholder that is refused in
        // Production and accepted in Staging is a placeholder that gets copied into Production by
        // the next person who reads the wrong file.
        ValidatePlaceholderSecrets(configuration);

        if (!environment.IsProduction())
            return;

        var monobankEnabled = ValidateMonobank(configuration);
        ValidateAuthentication(configuration);
        ValidateClientIntegrity(configuration);
        ValidateFulfilment(configuration, monobankEnabled);
        ValidateDatabase(configuration);
    }
}
