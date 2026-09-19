using System.Text;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace FuelFlow.API.Extensions;

internal static class AuthSetup
{
    private const string DevSecret = "test-secret-key-that-is-at-least-32-characters-long";
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

        var isTesting = config.GetValue<bool>("Testing");
        var jwtOptions = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        var secret = jwtOptions.Secret;
        if (string.IsNullOrWhiteSpace(secret) || secret == PlaceholderSecret)
        {
            if (environment.IsDevelopment() || isTesting)
            {
                secret = DevSecret;
            }
            else
            {
                throw new InvalidOperationException(
                    "Jwt:Secret is not configured. Set the 'Jwt:Secret' configuration value to a secure random key of at least 32 characters.");
            }
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
                ValidateIssuer = !isTesting,
                ValidateAudience = !isTesting,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidAudience = jwtOptions.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ClockSkew = TimeSpan.Zero,
            };
        });

        return services;
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
    /// a public endpoint into a 401 in production, and for the station list or the Monobank webhook
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
            .AddPolicy("Staff", policy =>
                policy.RequireRole("ProductOwner", "Admin", "Manager"))
            // Toggling an authentication mechanism is a higher-privilege action than routine staff
            // work, so Managers are excluded: only Admin and ProductOwner may change the QA
            // test-access switch. Read-only status stays under the broader "Staff" policy.
            .AddPolicy("QaTestAccessAdmin", policy =>
                policy.RequireRole("ProductOwner", "Admin"));

        return services;
    }
}
