using System.Text;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace FuelFlow.API.Extensions;

internal static class AuthSetup
{
    internal static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        services.PostConfigure<JwtOptions>(opts =>
        {
            opts.Secret ??= "test-secret-key-that-is-at-least-32-characters-long";
            opts.Issuer ??= "FuelFlow";
            opts.Audience ??= "FuelFlow";
        });

        services.AddScoped<IJwtTokenService, JwtTokenService>();

        var jwtOptions = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        jwtOptions.Secret ??= "test-secret-key-that-is-at-least-32-characters-long";
        jwtOptions.Issuer ??= "FuelFlow";
        jwtOptions.Audience ??= "FuelFlow";

        var isTesting = config.GetValue<bool>("Testing");

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
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
                ClockSkew = TimeSpan.Zero,
            };
        });

        return services;
    }
}
