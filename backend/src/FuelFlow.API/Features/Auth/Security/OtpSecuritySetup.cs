using System.Security.Cryptography;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace FuelFlow.Features.Auth.Security;

/// <summary>
/// The "refuse to boot" guard for the OTP pepper, and the one-time preparation of the QA code.
/// </summary>
/// <remarks>
/// The pepper is the half of the OTP key that must never reach the database. Without it a leaked
/// dump of <c>verification_codes</c> is a million SHA-256 guesses; with it, the dump alone is
/// useless. Production therefore cannot start without a real one - the same hard refusal the JWT
/// key and the voucher QR key get, for the same reason. Staging is allowed to run on a pepper
/// generated for that process (with a warning), so a merge does not fail a deploy over a value
/// that only protects a staging copy; Development keeps the committed
/// <see cref="AuthOptions.DevelopmentOtpPepper"/>, which is explicitly refused in Production.
/// </remarks>
internal static class OtpPepperValidator
{
    internal const int MinimumLength = 32;

    internal static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
            return;

        var pepper = configuration["Auth:OtpPepper"];

        if (environment.IsProduction() && IsMissing(pepper))
        {
            throw new InvalidOperationException(
                "Refusing to start: Auth:OtpPepper is not configured. A six-digit verification code "
                + "hashed with a bare SHA-256 is searchable offline in milliseconds, so the database "
                + "hash is only a secret when a server-side pepper is mixed in. Set Auth__OtpPepper "
                + "(OTP_PEPPER in the deploy env) to a random value of at least 32 characters "
                + "(openssl rand -base64 32).");
        }

        if (IsMissing(pepper))
            return;

        if (pepper!.Length < MinimumLength)
        {
            throw new InvalidOperationException(
                $"Refusing to start: Auth:OtpPepper is only {pepper.Length} characters long. The "
                + "pepper is the half of the key that never reaches the database, so it has to be "
                + "unguessable: use at least "
                + $"{MinimumLength} characters (openssl rand -base64 32).");
        }

        if (PlaceholderSecret.LooksLikePlaceholder(pepper))
        {
            throw new InvalidOperationException(
                "Refusing to start: Auth:OtpPepper still holds a placeholder value from the env "
                + "template. The pepper is published in the template, so a value that was never "
                + "replaced is a value everybody has, and it is the only thing that makes an OTP "
                + "hash unsearchable. Generate one with openssl rand -base64 32.");
        }
    }

    /// <summary>
    /// A pepper that is absent, blank, or the committed Development fallback. The fallback is
    /// checked explicitly because <see cref="AuthOptions"/> starts with it, so "not configured"
    /// and "configured to the public value" are the same thing by the time options are bound.
    /// </summary>
    private static bool IsMissing(string? pepper) =>
        string.IsNullOrWhiteSpace(pepper)
        || string.Equals(pepper, AuthOptions.DevelopmentOtpPepper, StringComparison.Ordinal);
}

internal static class OtpSecuritySetup
{
    /// <summary>
    /// Settles the effective OTP pepper and hashes the configured QA code with it, once.
    /// </summary>
    /// <remarks>
    /// Post-configured rather than configured, so it runs after the <c>Auth</c> section has been
    /// bound in <see cref="FuelFlow.API.Extensions.ServiceSetup"/>. The QA code is a verification
    /// code, so it is stored as the same peppered digest an OTP row uses; the plaintext is dropped
    /// after hashing, and an absent code leaves the hash null, which forces QA access off.
    /// </remarks>
    internal static IServiceCollection AddOtpSecurity(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        services.PostConfigure<AuthOptions>(options =>
        {
            var missing = string.IsNullOrWhiteSpace(options.OtpPepper);

            if (environment.IsDevelopment())
            {
                if (missing)
                    options.OtpPepper = AuthOptions.DevelopmentOtpPepper;
            }
            else if (missing
                     || string.Equals(options.OtpPepper, AuthOptions.DevelopmentOtpPepper, StringComparison.Ordinal))
            {
                // Production has already refused to start without a real pepper; reaching here is
                // Staging (or another non-production environment). An ephemeral pepper keeps the
                // deploy working without a hand-edited env file, at the cost of invalidating the
                // five-minute codes that were out when the process restarted.
                options.OtpPepper = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

                Log.Warning(
                    "SECURITY: no Auth:OtpPepper is configured, so a temporary pepper was generated "
                    + "for this process. Verification codes issued before a restart cannot be verified "
                    + "after it. Set OTP_PEPPER in the environment to keep them stable.");
            }

            if (!string.IsNullOrWhiteSpace(options.QaTestAccessCode))
            {
                options.QaTestAccessCodeHash =
                    OtpCodeHasher.Hash(options.QaTestAccessCode.Trim(), options.OtpPepper);
                options.QaTestAccessCode = null;
            }
        });

        return services;
    }
}
