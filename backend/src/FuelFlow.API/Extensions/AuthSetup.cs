using System.Text;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
}
