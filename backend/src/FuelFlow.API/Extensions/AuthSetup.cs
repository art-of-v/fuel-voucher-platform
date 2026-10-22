using System.Security.Cryptography;
using System.Text;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Errors;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Services;
using FuelFlow.SharedKernel.Security;
using FuelFlow.SharedKernel.Time;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace FuelFlow.API.Extensions;

internal static class AuthSetup
{
    private const string PlaceholderSecret = "CHANGE_THIS_TO_A_SECURE_RANDOM_KEY_IN_PRODUCTION_AT_LEAST_32_CHARACTERS";
    private const int MinSecretLength = 32;

    internal static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration config, IWebHostEnvironment environment)
    {
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        services.PostConfigure<JwtOptions>(opts =>
        {
            opts.Issuer ??= "FuelFlow";
            opts.Audience ??= "FuelFlow";
        });

        services.AddScoped<IJwtTokenService, JwtTokenService>();

        var jwtOptions = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        var secret = jwtOptions.Secret;
        if (string.IsNullOrWhiteSpace(secret) || secret == PlaceholderSecret)
        {
            // There is no committed fallback key any more. It used to be one constant - the same
            // string in every checkout, on every machine, forever - and it was reachable from
            // Production through a configuration switch named "Testing" that turned the issuer and
            // audience checks off as well. Both are gone: a key that is in the repository is a key
            // that is in everybody's repository, and a switch that opens the door in Production is
            // one environment variable away from being live. The development answer is a key that
            // is generated per process, so there is nothing to leak and nothing to share.
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Jwt:Secret is not configured. Set the 'Jwt:Secret' configuration value to a secure random key of at least 32 characters.");
            }

            secret = GenerateEphemeralSecret();

            Log.Warning(
                "SECURITY: Jwt:Secret is not configured, so a temporary signing key was generated for this "
                + "process. Every restart invalidates the tokens that were out, and no other machine can "
                + "forge one for this one. Set Jwt__Secret to keep sessions across restarts.");
        }

        if (secret.Length < MinSecretLength)
        {
            throw new InvalidOperationException(
                $"Jwt:Secret must be at least {MinSecretLength} characters long.");
        }

        services.Configure<JwtOptions>(opts => opts.Secret = secret);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                // Issuer and audience are always checked. They used to be switchable off by a
                // configuration key, which meant a token minted for another audience - or by
                // another service sharing the key - was accepted here. There is no switch.
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidAudience = jwtOptions.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ClockSkew = TimeSpan.Zero,
                // The lifetime is judged by the application's clock, not by the machine's. The
                // framework's own check reads the system clock, which is exactly what the test
                // suite cannot control: a token minted on the suite's fixed instant would be
                // judged against the real date and every authentication test would fail. In
                // production the two clocks are the same clock.
                LifetimeValidator = (notBefore, expires, _, _) => TokenLifetime.IsValid(notBefore, expires),
            };
        });

        return services;
    }

    /// <summary>
    /// A signing key for a development process that has none configured: 32 random bytes, base64.
    /// Generated per process rather than committed, so there is no key in the repository to copy,
    /// leak or share, and no key two machines have in common.
    /// </summary>
    private static string GenerateEphemeralSecret()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    /// <summary>
    /// Requires an authenticated user on every endpoint that does not explicitly opt out with
    /// <c>[AllowAnonymous]</c>.
    /// <para>
    /// Before this, authorization was opt-in: a controller or action with no <c>[Authorize]</c>
    /// attribute was anonymous. That is the wrong default for this API - FF-01 was exactly that
    /// mistake, an admin voucher catalogue including redeemable QR payloads readable without a
    /// token, created by forgetting one attribute. Every future feature slice inherits the safe
    /// default now: forgetting the attribute produces a 401 that the first smoke test catches,
    /// instead of a public endpoint that nothing catches.
    /// </para>
    /// <para>
    /// The failure mode of the fallback is the mirror image - a missed <c>[AllowAnonymous]</c> turns
    /// a public endpoint into a 401 in production, and for the Brand list or the Monobank webhook
    /// that means a visible outage or silently lost payment callbacks. The complete intended
    /// anonymous surface is therefore pinned by a test
    /// (<c>AnonymousEndpointSurfaceTests</c>) that enumerates routed endpoint metadata and fails on
    /// any addition or removal, so the set is a reviewed decision rather than a side effect.
    /// </para>
    /// <para>
    /// Middleware is unaffected: Swagger and the Hangfire dashboard are not routed endpoints, so
    /// they keep their own authorization filters.
    /// </para>
    /// </summary>
    internal static IServiceCollection AddDefaultAuthorizationPolicy(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())
            // The staff surface is split into CAPABILITIES, one policy per capability, and an
            // endpoint declares the capability it exercises rather than the fact that it is for
            // staff. Until 2026-10-21 every one of them carried the same "Staff" policy, so
            // "[Authorize(Policy = "Staff")]" on a refund and on a report said exactly the same
            // thing - and a policy that admits every staff role admits every staff role into
            // moving money. The finding is planning #521 (S-1).
            //
            // Role membership comes from SeedRoles (single source of truth) rather than repeated
            // string literals, so a new staff role is added in one place, not scattered here and
            // across every [Authorize(Roles = "...")] attribute.
            //
            // The read policy admits the staff list, so a role that joins the staff surface can
            // see it. The write capabilities carry their own member lists, so that same role
            // cannot write, move money, or administer accounts until somebody says so.
            .AddPolicy(AuthPolicies.Staff, policy =>
                policy.RequireRole(SeedRoles.StaffRoleNames))
            .AddPolicy(AuthPolicies.CatalogueWrite, policy =>
                policy.RequireRole(SeedRoles.CatalogueWriteRoleNames))
            .AddPolicy(AuthPolicies.VoucherWrite, policy =>
                policy.RequireRole(SeedRoles.VoucherWriteRoleNames))
            .AddPolicy(AuthPolicies.MoneyMovement, policy =>
                policy.RequireRole(SeedRoles.MoneyMovementRoleNames))
            .AddPolicy(AuthPolicies.SettingsWrite, policy =>
                policy.RequireRole(SeedRoles.SettingsWriteRoleNames))
            .AddPolicy(AuthPolicies.StaffOperations, policy =>
                policy.RequireRole(SeedRoles.StaffOperationsRoleNames))
            // Ban/unban/delete, the assignment of a role and the QA test-access switch are
            // higher-privilege than routine staff work, so Managers are excluded: only Admin and
            // ProductOwner qualify.
            .AddPolicy(AuthPolicies.UserAdministration, policy =>
                policy.RequireRole(SeedRoles.AdminAndOwnerRoleNames))
            .AddPolicy(AuthPolicies.QaTestAccess, policy =>
                policy.RequireRole(SeedRoles.AdminAndOwnerRoleNames))
            // The redeemable payload of a voucher is a bearer instrument: whoever holds it can
            // spend the customer's fuel, and nothing records that they did. It is narrower than
            // the staff surface for that reason - a Manager reads the stock, the Admin tier
            // holds the code.
            .AddPolicy(AuthPolicies.VoucherPayload, policy =>
                policy.RequireRole(SeedRoles.VoucherPayloadRoleNames))
            // The settlement code is the identifier the settlement channel resolves money to, so
            // it is a decision about money and not about the lists. It is the money tier - the
            // roles that may place a payment for a customer (planning #530, S-10) - and it is
            // listed apart from the read surface, so joining the lists does not bring it along.
            .AddPolicy(AuthPolicies.SettlementCode, policy =>
                policy.RequireRole(SeedRoles.SettlementCodeRoleNames));

        // A 401 and a 403 answer with an empty body by default, which would be a second error
        // shape. They leave as the one envelope instead - the headers are untouched.
        // Replace, not Add: the framework registers its own handler with TryAdd, so a second
        // registration would be ignored and the empty body would stay.
        services.Replace(ServiceDescriptor.Singleton<
            IAuthorizationMiddlewareResultHandler,
            ProblemDetailsAuthorizationResultHandler>());

        return services;
    }
}
