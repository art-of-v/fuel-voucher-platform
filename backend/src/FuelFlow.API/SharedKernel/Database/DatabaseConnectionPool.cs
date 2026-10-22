using Microsoft.Extensions.Configuration;

namespace FuelFlow.SharedKernel.Database;

/// <summary>
/// One cap for every connection pool a process opens against Postgres, and one decision about
/// what a returned connection keeps.
/// </summary>
/// <remarks>
/// <para>
/// A process opens more than one pool: the application's own data source, the Hangfire storage
/// and the Postgres health check - and the worker opens its own set. Npgsql's default cap is 100
/// per pool, so "the application's pool is capped at 5" protected only one of them: a Hangfire
/// burst plus a health probe could reach max_connections (50 on the production server) long
/// before that budget was anywhere near. Every pool in the process now gets the same cap, and the
/// cap is one configuration value - <c>Database:MaxPoolSize</c>.
/// </para>
/// <para>
/// The second thing is <c>No Reset On Close</c>. When a pooled connection goes back to the pool,
/// Npgsql resets it - <c>DISCARD ALL</c>, or the equivalent series of commands when there are
/// prepared statements. <b>The reset is not a round trip of its own</b>: it is written into
/// Npgsql's buffer and sent with the next statement, so what it costs is the SERVER a statement
/// execution. The production review of 2026-10-20 counted <b>91 621</b> of them, which is 91 621
/// statements the database was asked to execute to do nothing.
/// </para>
/// <para>
/// <b>Why it is safe here, and where it is not.</b> Npgsql's own documentation says the cost of
/// this setting is "leaking state": a connection returned to the pool keeps whatever the last
/// user left on it. This application leaves nothing behind that is not deliberately overwritten.
/// The row-level policies read two session variables, <c>app.user_id</c> and
/// <c>app.is_system</c>; <see cref="TenantConnectionInterceptor"/> writes both on every open -
/// including the anonymous and system cases - so a connection can never hand the next request
/// the previous request's identity. There is no other session-level <c>SET</c>, no temporary
/// table and no <c>LISTEN</c>, and the only advisory locks it takes are transaction-scoped
/// (<c>pg_advisory_xact_lock</c>), which the transaction's end releases. The connection string
/// also does not set <c>Application Name</c>, so no connection inherits another one's identity.
/// A transaction still open when the connection is returned is rolled back by Npgsql, which this
/// setting does not change.
/// </para>
/// <para>
/// <b>The Hangfire storage is the exception, and it is not a small one.</b> The Hangfire
/// PostgreSQL storage sets a session variable of its own (<c>expireat</c>) and LISTENs on the
/// connection it is given, and that state belongs to a library whose SQL is not ours to read. A
/// connection that came back with it still on it would hand the next user a storage that
/// believes it is in a state it is not. That pool keeps the reset - see
/// <see cref="WithMaxPoolSize"/>.
/// </para>
/// <para>
/// An explicit value in the connection string always wins, for the cap and for the reset - the
/// operator's string is the one that knows.
/// </para>
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
    /// Returns the connection string with the pool settings applied: the cap, and the decision to
    /// leave a returned connection's session state alone. A connection string that already
    /// carries either of them is left alone: the operator's explicit value wins.
    /// </summary>
    /// <remarks>
    /// <b>Use this only for pools whose SQL is the application's own.</b> The Hangfire storage
    /// sets a session variable of its own and LISTENs, so a connection that came back with that
    /// state still on it would hand the next user a storage that believes it is in a state it is
    /// not. That pool gets the cap and nothing else - see <see cref="WithMaxPoolSize"/>.
    /// </remarks>
    public static string WithPoolSettings(string connectionString, int maxPoolSize)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var result = connectionString;

        if (maxPoolSize > 0 && !result.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase))
        {
            result = $"{result.TrimEnd(';')};Maximum Pool Size={maxPoolSize}";
        }

        if (!result.Contains("No Reset On Close", StringComparison.OrdinalIgnoreCase))
        {
            result = $"{result.TrimEnd(';')};No Reset On Close=true";
        }

        return result;
    }

    /// <summary>
    /// Returns the connection string with the pool cap applied, and with the connection reset
    /// left ON. This is the pool of a library whose SQL is not ours to read.
    /// </summary>
    /// <remarks>
    /// The Hangfire PostgreSQL storage sets a session variable of its own and LISTENs on the
    /// connection it is given. Npgsql's reset (<c>DISCARD ALL</c>) is what stops that state from
    /// reaching the next user of the same physical connection, so this pool keeps the reset.
    /// </remarks>
    public static string WithMaxPoolSize(string connectionString, int maxPoolSize)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || maxPoolSize <= 0)
        {
            return connectionString;
        }

        if (connectionString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        return $"{connectionString.TrimEnd(';')};Maximum Pool Size={maxPoolSize}";
    }
}
