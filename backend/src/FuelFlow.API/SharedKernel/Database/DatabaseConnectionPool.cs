using Microsoft.Extensions.Configuration;

namespace FuelFlow.SharedKernel.Database;

/// <summary>
/// One cap for every connection pool a process opens against Postgres.
/// </summary>
/// <remarks>
/// A process opens more than one pool: the application's own data source, the Hangfire storage
/// and the Postgres health check - and the worker opens its own set. Npgsql's default cap is 100
/// per pool, so "the application's pool is capped at 5" protected only one of them: a Hangfire
/// burst plus a health probe could reach max_connections (50 on the production server) long
/// before that budget was anywhere near. Every pool in the process now gets the same cap, and the
/// cap is one configuration value - <c>Database:MaxPoolSize</c>.
/// </remarks>
public static class DatabaseConnectionPool
{
    /// <summary>The cap the application has always used for its own data source.</summary>
    public const int DefaultMaxPoolSize = 5;

    /// <summary>Configuration key for the cap, so ops can raise it without a code change.</summary>
    public const string MaxPoolSizeConfigurationKey = "Database:MaxPoolSize";

    /// <summary>Reads the cap from the configuration, falling back to the default.</summary>
    public static int ResolveMaxPoolSize(IConfiguration configuration)
    {
        var configured = configuration.GetValue(MaxPoolSizeConfigurationKey, DefaultMaxPoolSize);
        return configured > 0 ? configured : DefaultMaxPoolSize;
    }

    /// <summary>
    /// Returns the connection string with the pool cap applied. A connection string that already
    /// carries <c>Maximum Pool Size</c> is left alone: the operator's explicit value wins.
    /// </summary>
    public static string WithMaxPoolSize(string connectionString, int maxPoolSize)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || maxPoolSize <= 0)
            return connectionString;

        if (connectionString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase))
            return connectionString;

        return $"{connectionString.TrimEnd(';')};Maximum Pool Size={maxPoolSize}";
    }
}
