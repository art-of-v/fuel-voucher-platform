using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FuelFlow.API.Extensions;

internal static partial class SecurityConfigurationValidator
{
    /// <summary>
    /// The two database rules: the connection verifies the server's certificate, and the
    /// application's traffic does not connect as the role that can change the schema.
    /// </summary>
    private static void ValidateDatabase(IConfiguration configuration)
    {
        // The database transport. Everything else in this file guards a control that is OFF by
        // default; this one guards a control that is ON by default, which is what makes it easy
        // to lose: "SSL Mode=Require" encrypts the session against anyone, and "Trust Server
        // Certificate=true" accepts any certificate at all - so a host that can present a
        // self-signed certificate terminates the session and reads the refresh-token hashes, the
        // personal data and the voucher ciphertext. "SSL Mode=Disable" is the same thing without
        // the encryption.
        //
        // The container-to-container connection used to be the reason for it (the local Postgres
        // had no certificate), and the answer was to keep the default for everybody so that a
        // managed database could never inherit it by accident. The certificates exist now, so
        // Production states a verified transport, and a string that says otherwise is a refusal
        // rather than a warning: the connection carries money and personal data, and the operator
        // has to say which transport they meant.
        //
        // "VerifyFull" is the only mode that checks WHO is on the other end, so it is the only
        // one that passes. A string with no SSL Mode at all also fails: the code would then
        // append "SSL Mode=Require;Trust Server Certificate=true" to it, which is the old default
        // wearing a different name.
        var connectionString = configuration["Database:ConnectionString"] ?? "";
        var migrationConnectionString = configuration["Database:MigrationConnectionString"] ?? "";

        // BOTH connections carry the money and the personal data - the migration reads the
        // schema and may touch the rows a backfill rewrites - so both have to verify the
        // server. The migration connection used to not exist; now it does, and it is the
        // one an operator is most likely to leave at the old default.
        foreach (var (settingName, value) in new[]
                 {
                     ("Database:ConnectionString", connectionString),
                     ("Database:MigrationConnectionString", migrationConnectionString),
                 })
        {
            if (value.Length > 0
                && !value.Contains("SSL Mode=VerifyFull", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Refusing to start: the database connection string does not verify the server's "
                    + "certificate. In Production the connection must be "
                    + "'SSL Mode=VerifyFull' with the CA ('Root Certificate=<path to ca.crt>'): "
                    + "'Require' and 'Trust Server Certificate=true' encrypt the session against anyone, "
                    + "and 'Disable' does not encrypt it at all - and this connection carries the money and "
                    + $"the personal data. (The string that was refused is {settingName}.)");
            }
        }

        // The application's traffic and the schema migration must not share a role.
        //
        // One role used to serve both, and it was the cluster's SUPERUSER - the one role the
        // postgres image creates. So a defect that reached the database did not "leak a
        // table", it owned the cluster: it could read every role's password hash out of
        // pg_authid, create a new superuser, drop the database, or write straight into the
        // audit trail the money reconciliation depends on. The trail was protected by the
        // application's own discipline rather than by the database's privileges.
        //
        // The roles are separated now: the traffic runs as a role that may do the
        // application's work and nothing else, and the migration runs as another one that
        // may change the schema and nothing else. This is the check that the separation is
        // real rather than a naming convention - a configuration that points both strings at
        // one role puts the whole thing back, and it does so silently.
        if (connectionString.Length > 0)
        {
            // The migration connection string is only required when the application MIGRATES
            // at boot. A deployment that manages the schema by hand (RunMigrationsOnBoot=false)
            // has no migration to run, and refusing it to start over a connection string it
            // will never open would be a refusal of the wrong thing.
            var runMigrationsOnBoot = configuration.GetValue<bool?>("RunMigrationsOnBoot") ?? true;

            if (runMigrationsOnBoot && migrationConnectionString.Length == 0)
            {
                throw new InvalidOperationException(
                    "Refusing to start: Database:MigrationConnectionString is not set. The "
                    + "application's traffic and the schema migration must not share a role - the "
                    + "traffic runs as a role that can only do the application's own work, and the "
                    + "migration runs as another one that can change the schema. Point "
                    + "Database__MigrationConnectionString at the migration role (deploy/"
                    + "ensure-database-roles.sh creates both).");
            }

            if (migrationConnectionString.Length == 0)
            {
                // The schema is managed by hand; there is no second role to check.
                return;
            }

            var applicationRole = RoleOf(connectionString);
            var migrationRole = RoleOf(migrationConnectionString);

            if (applicationRole is null || migrationRole is null)
            {
                throw new InvalidOperationException(
                    "Refusing to start: one of the two database connection strings does not name "
                    + "a role, so it cannot be told whether the application's traffic and the "
                    + "schema migration share one. Both strings must carry a 'Username='.");
            }

            if (string.Equals(applicationRole, migrationRole, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Refusing to start: the application's traffic and the schema migration both "
                    + $"connect as the role '{applicationRole}'. One role for both is what made a "
                    + "defect that reached the database able to own the cluster; the traffic "
                    + "belongs to a role that may do the application's work and nothing else, "
                    + "and the schema belongs to another one.");
            }
        }
    }

    /// <summary>
    /// The role a connection string connects as, or <c>null</c> when it does not name one.
    /// </summary>
    /// <remarks>
    /// Read through Npgsql's own parser rather than with a regex, so a string this code
    /// cannot read is a refusal rather than a guess: the parser is the one that will read
    /// the string when the connection opens.
    /// </remarks>
    private static string? RoleOf(string connectionString)
    {
        try
        {
            var username = new NpgsqlConnectionStringBuilder(connectionString).Username;
            return string.IsNullOrWhiteSpace(username) ? null : username;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
