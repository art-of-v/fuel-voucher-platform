using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FuelFlow.API.Extensions;

/// <summary>
/// What the application's own database role is allowed to be.
/// </summary>
/// <remarks>
/// <para>
/// One PostgreSQL role used to serve both the application's traffic and the schema
/// migrations, and that role was the cluster's SUPERUSER: the one role the
/// <c>postgres:16-alpine</c> image creates, with the password that sits in deploy/.env.
/// </para>
/// <para>
/// There is no SQL injection in this codebase today - which is why this is a LOW-MEDIUM
/// finding and not a HIGH one - but the consequence of any future defect that reaches the
/// database is not "an API bug leaks a table". It is "an API bug owns the cluster": read
/// every role's password hash out of <c>pg_authid</c>, create a new superuser, drop the
/// database, or write straight into <c>audit_events</c> and <c>voucher_events</c> to falsify
/// the trail the money reconciliation depends on. The trail was protected by the
/// application's own discipline, not by the database's privileges.
/// </para>
/// <para>
/// This guard asks the LIVE catalogue what the connected role actually is, rather than
/// reading the role's NAME out of the connection string. The name is a claim; the
/// catalogue is the fact. A role called <c>fuelflow_app</c> that happens to be a superuser
/// is refused exactly like a role called <c>postgres</c>, and a role that was created as a
/// plain login and promoted later is caught on the next boot.
/// </para>
/// <para>
/// The migration runs as its own role, one that may change the schema and may not do
/// anything else. The application's traffic runs as a role that may do neither. The
/// separation is what makes the finding's threat model wrong: a defect that reaches the
/// application's connection can read and write the application's own tables - which it
/// could do anyway - and cannot touch the cluster, the roles, or the schema.
/// </para>
/// </remarks>
internal static class DatabaseRoleValidator
{
    /// <summary>
    /// The catalogue query. <c>pg_roles</c> is readable by every role, and it masks the
    /// password hashes - this asks only about the privileges, never about the secrets.
    /// </summary>
    internal const string CatalogueQuery =
        "SELECT rolname, rolsuper, rolcreaterole, rolcreatedb, rolbypassrls " +
        "FROM pg_roles WHERE rolname = current_user";

    /// <summary>
    /// What the catalogue says about the role the application is connected as. A record
    /// rather than a bag of booleans, so the refusal can name the role it is refusing.
    /// </summary>
    internal sealed record RoleAttributes(
        string Name,
        bool IsSuperuser,
        bool CanCreateRole,
        bool CanCreateDatabase,
        bool BypassesRowSecurity);

    /// <summary>
    /// The reason the application must not start against this role, or <c>null</c> when the
    /// role is one the application may run as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The four attributes are four different ways of owning the cluster:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>SUPERUSER</b> - bypasses every permission check in the database. It can read
    /// <c>pg_authid</c> (every role's password hash), create roles, and do anything at all.
    /// </description></item>
    /// <item><description>
    /// <b>CREATEROLE</b> - can create a role and make itself (or a role it creates) a
    /// superuser. That is the escalation this finding is about, one step removed.
    /// </description></item>
    /// <item><description>
    /// <b>CREATEDB</b> - can create another database on the same cluster and read whatever
    /// is in it, which on a shared cluster is somebody else's data.
    /// </description></item>
    /// <item><description>
    /// <b>BYPASSRLS</b> - reads past the row-level security the tables carry. There is no
    /// RLS on the tables today, so this one bites the day somebody adds it.
    /// </description></item>
    /// </list>
    /// </remarks>
    internal static string? RefusalReason(RoleAttributes role)
    {
        if (role.IsSuperuser)
        {
            return $"the database role '{role.Name}' is a SUPERUSER. A superuser bypasses "
                + "every permission check in the database: it can read every role's password "
                + "hash out of pg_authid, create another superuser, drop the database, and "
                + "write straight into the audit trail the money reconciliation depends on. "
                + "The application's traffic must run as a role that can only do the "
                + "application's own work - deploy/ensure-database-roles.sh creates that role.";
        }

        if (role.CanCreateRole)
        {
            return $"the database role '{role.Name}' may CREATE ROLE, so it can create "
                + "itself a superuser and own the cluster that way. The application's "
                + "traffic must run as a role that cannot create roles.";
        }

        if (role.CanCreateDatabase)
        {
            return $"the database role '{role.Name}' may CREATE DATABASE, so it can create "
                + "a database of its own on this cluster and read what is in it. The "
                + "application's traffic must run as a role that cannot create databases.";
        }

        if (role.BypassesRowSecurity)
        {
            return $"the database role '{role.Name}' may BYPASS ROW LEVEL SECURITY, so it "
                + "reads past the row-level security the tables carry. The application's "
                + "traffic must run as a role that does not bypass it.";
        }

        return null;
    }

    /// <summary>
    /// Refuses to let a production build run against a role that owns the cluster.
    /// </summary>
    /// <remarks>
    /// Production only, for the same reason the rest of the startup guards are: the
    /// development and staging stacks connect as the cluster's own superuser on purpose
    /// (staging is a disposable box behind an HTTP Basic credential), and a guard that
    /// fires there stops work rather than making it safer. The check that matters is the
    /// one on the box that holds the money and the personal data.
    /// </remarks>
    internal static async Task ValidateAsync(
        NpgsqlDataSource dataSource,
        IHostEnvironment environment,
        ILogger logger)
    {
        if (!environment.IsProduction())
            return;

        RoleAttributes? role;
        try
        {
            await using var command = dataSource.CreateCommand(CatalogueQuery);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new InvalidOperationException(
                    "Refusing to start: the database did not report the role this "
                    + "application is connected as, so the role's privileges cannot be "
                    + "checked. The application's traffic must run as a role that is not a "
                    + "superuser, and the only way to know that is to ask the catalogue.");
            }

            role = new RoleAttributes(
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4));
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Refusing to start: the privileges of the database role this application "
                + "is connected as could not be read from the catalogue. The application's "
                + "traffic must run as a role that is not a superuser, and an unchecked role "
                + "is exactly what this guard exists for.", ex);
        }

        var reason = RefusalReason(role);
        if (reason is not null)
        {
            throw new InvalidOperationException("Refusing to start: " + reason);
        }

        logger.LogInformation(
            "Database role {DatabaseRole} checked against the catalogue: not a superuser, "
            + "and it may not create roles, databases, or bypass row-level security.",
            role.Name);
    }

    /// <summary>
    /// The startup call: ask the catalogue what role this process is connected as, and
    /// refuse to serve traffic if it is one that owns the cluster.
    /// </summary>
    internal static async Task ValidateDatabaseRoleAsync(this WebApplication app)
    {
        var dataSource = app.Services.GetRequiredService<NpgsqlDataSource>();
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        await ValidateAsync(dataSource, app.Environment, logger);
    }
}
