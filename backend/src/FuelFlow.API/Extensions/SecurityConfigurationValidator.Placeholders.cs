using FuelFlow.SharedKernel.Security;
using Microsoft.Extensions.Configuration;

namespace FuelFlow.API.Extensions;

internal static partial class SecurityConfigurationValidator
{
    /// <summary>
    /// A secret that is still the placeholder from the env template is a secret in a public
    /// repository, and it is refused here - outside Development, in Production and in Staging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The finding this answers is planning #620 (S-11). The template
    /// <c>deploy/.env.production.example</c> ships placeholder secrets, and the application
    /// accepted every one of them as a real value:
    /// <c>POSTGRES_PASSWORD=CHANGE_ME_openssl_rand_base64_36</c>,
    /// <c>REDIS_PASSWORD=CHANGE_ME_openssl_rand_hex_32</c>,
    /// <c>JWT_SECRET=CHANGE_ME_at_least_32_characters_long_random_string</c> and
    /// <c>QR_CIPHER_KEY=CHANGE_ME_openssl_rand_base64_32</c>. The JWT one is the serious one: it
    /// signs the access tokens, it is 53 characters long (so it passed the length check), and the
    /// guard that was supposed to catch it compared the value against a different literal. The
    /// repository is public until 2026-11-01, so a token signed with it can be forged by anybody.
    /// </para>
    /// <para>
    /// The check is the PATTERN, not one literal - a literal list is a list that goes stale, and the
    /// stale one is the reason this exists. The deploy template is the other half: preflight.sh
    /// compares the deployed env file against the template's own values verbatim, so a template that
    /// is edited to hide the placeholder does not defeat the check.
    /// </para>
    /// <para>
    /// The passwords are not settings of their own: the database password and the Redis password
    /// reach the application inside the connection strings that carry them, so they are read from
    /// there. That is the only place the application can see them - and it is enough, because a
    /// placeholder password that reaches the connection string is a placeholder password that is
    /// live. Both database connections are read: the traffic's and the migration's, because the
    /// role that can change the schema is a role that can change the data in it.
    /// </para>
    /// </remarks>
    private static void ValidatePlaceholderSecrets(IConfiguration configuration)
    {
        RefusePlaceholder(
            configuration["Jwt:Secret"],
            "Jwt:Secret",
            "it signs every access token. A token signed with it can be forged by anybody, which "
            + "is full staff console access - refunds, payment placement, customer PII and the "
            + "redeemable voucher payloads - with the whole session model bypassed, because the "
            + "forger never holds a refresh token.");

        RefusePlaceholder(
            configuration["QrCipher:Key"],
            "QrCipher:Key",
            "it encrypts the redeemable QR payload of every voucher at rest, and it is the only "
            + "thing that can read it.");

        RefusePlaceholder(
            DatabasePassword(configuration["Database:ConnectionString"]),
            "the database password inside Database:ConnectionString (POSTGRES_APP_PASSWORD)",
            "it is the credential the application's traffic opens the database with, and the "
            + "database holds the money, the personal data and the voucher ciphertext.");

        RefusePlaceholder(
            DatabasePassword(configuration["Database:MigrationConnectionString"]),
            "the database password inside Database:MigrationConnectionString (POSTGRES_MIGRATOR_PASSWORD)",
            "it is the credential the schema migration opens the database with, and a role that "
            + "can change the schema is a role that can change the data in it.");

        RefusePlaceholder(
            RedisPassword(configuration["ConnectionStrings:Redis"]),
            "the Redis password inside ConnectionStrings:Redis (REDIS_PASSWORD)",
            "it is the credential the application opens Redis with, and Redis holds the cached "
            + "sessions.");
    }

    /// <summary>
    /// Refuses one placeholder value, naming the setting so the operator knows which line of the
    /// env file to fix.
    /// </summary>
    private static void RefusePlaceholder(string? value, string setting, string consequence)
    {
        if (!PlaceholderSecret.LooksLikePlaceholder(value))
            return;

        throw new InvalidOperationException(
            $"Refusing to start: {setting} still holds a placeholder value. {consequence} "
            + "The env templates ship placeholder values on purpose - they are instructions, not "
            + "secrets - and they are published in a public repository, so a value that was never "
            + "replaced is a value everybody has. Generate a real one (openssl rand -base64 36 for "
            + "the database and the JWT key, openssl rand -hex 32 for Redis, openssl rand -base64 32 "
            + "for the QR cipher key) and put it in the env file on the box.");
    }

    /// <summary>
    /// The password inside an Npgsql-style connection string, which is how the database password
    /// reaches the application: <c>Host=...;Password=...;SSL Mode=...</c>.
    /// </summary>
    private static string? DatabasePassword(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        // The URI form (postgresql://user:password@host/db) carries it in the user info.
        if (connectionString.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            var parts = uri.UserInfo.Split(':', 2);
            return parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null;
        }

        return ValueOf(connectionString, "Password", ';');
    }

    /// <summary>
    /// The password inside a Redis connection string: <c>redis:6379,password=...</c>, or the URI
    /// form <c>redis://:password@host:6379</c>.
    /// </summary>
    private static string? RedisPassword(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        if (connectionString.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            var parts = uri.UserInfo.Split(':', 2);
            return parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null;
        }

        return ValueOf(connectionString, "password", ',');
    }

    /// <summary>
    /// The value of one key in a <c>key=value</c> connection string, where the pairs are separated
    /// by the given character. A quoted value is unquoted, the way Npgsql reads it.
    /// </summary>
    private static string? ValueOf(string connectionString, string key, char separator)
    {
        foreach (var pair in connectionString.Split(separator))
        {
            var cut = pair.IndexOf('=');
            if (cut <= 0) continue;

            if (!pair[..cut].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = pair[(cut + 1)..].Trim();
            if (value.Length >= 2
                && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"')))
            {
                value = value[1..^1];
            }

            return value;
        }

        return null;
    }
}
