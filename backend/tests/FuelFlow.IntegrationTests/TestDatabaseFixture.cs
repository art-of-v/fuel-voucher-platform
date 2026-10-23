using FuelFlow.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// One <b>Postgres container for the whole suite</b>, one fully-migrated schema in it, and one
/// throwaway database cloned from that schema per test class.
/// </summary>
/// <remarks>
/// <para>
/// xUnit builds a fresh <see cref="TestDatabaseFixture"/> for every <c>IClassFixture</c> test
/// class, so this fixture used to start its own container and replay all 88 EF migrations from
/// zero for each of the 56 classes. Measured on the same machine: <b>4.2s</b> to start a container
/// and <b>5.9s</b> to apply the 88 migrations - about <b>10s per class, ~9 of the 13m23s</b> the
/// integration job spent in CI, spent building a database that was then thrown away and rebuilt
/// identically for the next class.
/// </para>
/// <para>
/// The database is therefore built <b>once</b> and frozen with
/// <c>ALTER DATABASE ... datistemplate = true</c>; each class then gets
/// <c>CREATE DATABASE ... TEMPLATE</c> of it, which costs a fraction of a second. That keeps the
/// property the suite actually depends on - <b>every class starts against a virgin,
/// fully-migrated database</b>, exactly as before - rather than sharing one mutable database
/// between classes. Several tests are written against that guarantee (they assert on rows a
/// migration seeded, or on a table being empty, without seeding or truncating first), and a
/// single shared database would have made those pass or fail purely on execution order.
/// </para>
/// <para>
/// <c>datistemplate</c> also sets <c>datallowconn = false</c>, so nobody - including a pooled
/// connection left over from the migration run - can hold a session open on the template. Without
/// that, a <c>CREATE DATABASE ... TEMPLATE</c> would intermittently fail with "source database is
/// being accessed by other users", which is the kind of failure that only shows up on CI.
/// </para>
/// </remarks>
public sealed class TestDatabaseFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// Environment-variable form of <c>Database:ConnectionString</c>. It has to be an environment
    /// variable, not <c>ConfigureAppConfiguration</c>/<c>UseSetting</c>: <c>Program.cs</c> reads
    /// <c>builder.Configuration.BuildConnectionString()</c> at line 48, before
    /// <c>builder.Build()</c>, and WebApplicationFactory's builder callbacks only run when
    /// <c>Build()</c> is intercepted - i.e. after that read has already happened.
    /// <c>WebApplication.CreateBuilder</c> does call <c>AddEnvironmentVariables()</c>, so a process
    /// env var set before the host boots is the one channel that lands in time.
    /// </summary>
    private const string ConnectionStringVariable = "Database__ConnectionString";

    /// <summary>
    /// Environment-variable form of <c>Database:MaintenanceConnectionString</c>. The host reads it
    /// at <c>Program.cs</c>:48, so it must be set as a process variable before any host builds -
    /// the maintenance role is what <see cref="DataRetentionService"/> resolves through DI.
    /// </summary>
    private const string MaintenanceConnectionStringVariable = "Database__MaintenanceConnectionString";

    /// <summary>
    /// Environment-variable form of <c>Database:MigrationConnectionString</c>. The migration is the
    /// one thing that may change the schema, so it keeps the cluster's own credential here - the
    /// traffic connection above is the application role, which may not.
    /// </summary>
    private const string MigrationConnectionStringVariable = "Database__MigrationConnectionString";

    /// <summary>
    /// Environment-variable form of <c>Observability:Loki:Enabled</c>. appsettings.Development.json
    /// enables the Loki sink, and <c>LoggingSetup.ConfigureFuelFlowLogging</c> fails closed when
    /// it is enabled without credentials (<c>LokiOptions.ValidateCredentials</c>) - correct for a real
    /// deployment, fatal for the test host, which boots under Development but has no Loki to push to
    /// and no credentials. Disabled via the same channel as the connection string above: a process
    /// env var set before the host is built, since <c>Program.cs</c> reads config before
    /// WebApplicationFactory's callbacks run.
    /// </summary>
    private const string LokiEnabledVariable = "Observability__Loki__Enabled";

    /// <summary>
    /// Environment-variable form of <c>ConnectionStrings:Redis</c>, which is the channel
    /// <c>Program.cs:121</c> reads and the same one deploy/docker-compose.prod.yml:136 sets. No new
    /// plumbing, and no test-only branch in production code.
    /// </summary>
    private const string RedisConnectionStringVariable = "ConnectionStrings__Redis";

    /// <summary>Pinned to the same major the production compose file runs.</summary>
    private const string RedisImage = "redis:7-alpine";

    /// <summary>
    /// The database the container is created with, migrated, and then frozen as a template. Named
    /// as before so a developer's muscle memory and the container's own default still line up.
    /// </summary>
    private const string TemplateDatabaseName = "fuelflow_test";

    /// <summary>The maintenance database, used for the two statements that cannot run inside the
    /// database they target (<c>ALTER DATABASE</c> rejects a session connected to itself).</summary>
    private const string MaintenanceDatabaseName = "postgres";

    /// <summary>
    /// Started at most once per test process. <see cref="Lazy{T}"/> with
    /// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/> rather than a plain field: xUnit
    /// constructs fixtures from several threads even inside a non-parallel collection, and
    /// <c>Lazy&lt;Task&gt;</c> guarantees the expensive start+ migrate runs once for all of them.
    /// </summary>
    private static readonly Lazy<Task<PostgreSqlContainer>> SuiteContainer =
        new(StartSuiteContainerAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Names the per-class databases <c>fuelflow_it_1..N</c>; Interlocked because the
    /// ordinal is handed out from fixture construction on several threads.</summary>
    private static int nextDatabaseOrdinal;

    /// <summary>
    /// The suite's one Redis container. Started together with <see cref="SuiteContainer"/> and
    /// shared by every class, with no per-class reset.
    /// </summary>
    private static readonly Lazy<Task<RedisContainer>> SuiteRedis =
        new(StartSuiteRedisAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The suite-wide container. Still exposed so a test that needs the container itself (rather
    /// than a database inside it) can reach it - but <see cref="ConnectionString"/> is what a test
    /// should use for its queries: it points at this class's own clone, not at the template.
    /// </summary>
    public PostgreSqlContainer DbContainer { get; private set; } = null!;

    /// <summary>
    /// Connection string for THIS class's private clone of the migrated schema, with SSL off.
    /// Every test that used to call <c>_fixture.ConnectionString</c> now calls
    /// this - the container's own string would point at the template database, which is frozen and
    /// un-connectable.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Connection string for THIS class's private clone, as <c>fuelflow_app</c> - the role
    /// production traffic runs as. Tests that connect over TCP as the application role use
    /// this; the test host itself keeps the superuser string.
    /// </summary>
    public string AppConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Connection string for THIS class's private clone, as <c>fuelflow_maintenance</c> - the
    /// role the retention purge runs on.
    /// </summary>
    public string MaintenanceConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// A context on THIS class's private clone connected as <c>fuelflow_maintenance</c> - what
    /// <see cref="DataRetentionService"/> resolves through DI in production.
    /// </summary>
    public ApplicationDbContext CreateMaintenanceContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(MaintenanceConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// A context on THIS class's private clone connected as the cluster's owner - the role the
    /// test harness itself works through.
    /// </summary>
    /// <remarks>
    /// The harness sets the stage; the application under test is the thing that runs as
    /// <c>fuelflow_app</c>, and the suite proves that the traffic role can do everything
    /// production asks of it. TRUNCATE and DDL are staging operations, and they are deliberately
    /// NOT privileges the traffic role has: a TRUNCATE on <c>payments</c> would let a single
    /// defect erase the ledger in one statement, which is the very thing the ledger's
    /// immutability rules exist to prevent. A harness that reset as the traffic role would
    /// therefore fail on its own reset rather than on the application's behaviour - and the
    /// obvious way to make it pass would be to hand the traffic role the privilege.
    /// <para>
    /// Reading through this context also sees every row: the owner bypasses the row-level
    /// policies the traffic role lives under. That is what a probe that looks into the database
    /// while a request is still running needs - a unit of work opened during a request, but
    /// outside the request's own scope, resolves to the anonymous tenant and would see nothing.
    /// </para>
    /// </remarks>
    public ApplicationDbContext CreateOwnerContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options, TestQrCipher.Instance);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development env so startup-time config reads (AddJwtAuth, AddDatabase) resolve the
        // development settings instead of throwing on a missing Jwt:Secret.
        builder.UseEnvironment("Development");

        // No DbContext override here on purpose. Redirecting only DbContextOptions left the rest
        // of the composition root pointed at appsettings.Development.json's Host=localhost;Port=5433:
        // the singleton NpgsqlDataSource and IOptions<DatabaseOptions> built by AddDatabase
        // (Program.cs:50) and Hangfire's Postgres storage (Program.cs:62-63) both capture the
        // connection string read at Program.cs:48. AddHangfireServer then starts a server that must
        // connect, so the suite silently required a developer's local Postgres on 5433 and hung or
        // failed in CI. The env var set in InitializeAsync redirects all three at once.
    }

    public async Task InitializeAsync()
    {
        DbContainer = await SuiteContainer.Value;
        await ReapPreviousClassesAsync();
        ConnectionString = await CloneTemplateDatabaseAsync();

        // ";SSL Mode=Disable" is mandatory, not cosmetic: BuildConnectionString appends
        // ";SSL Mode=Require;Trust Server Certificate=true" to any string that does not already
        // mention SSL Mode (DatabaseSetup.cs:34-35), and the test container serves no TLS.
        //
        // Set before any host is built, and repointed per class because the database is per class.
        // Safe to mutate despite being process-global: every integration test class is in the
        // [CollectionDefinition("Integration Tests", DisableParallelization = true)] collection,
        // so classes never overlap and the previous class's host - which captured the previous
        // value at Program.cs:48 - is disposed before the next one boots.
        //
        // The three connection strings are set by CloneTemplateDatabaseAsync, because each one
        // names a different role and each class has its own database.

        // The voucher QR cipher refuses to construct without a key, by design - a default key would
        // be a key every deployment shares. Tests get a fixed one for the same reason the QA access
        // code is fixed below: determinism. Nothing encrypted in a test container survives it.
        Environment.SetEnvironmentVariable(
            "QrCipher__Key",
            TestQrCipher.KeyBase64);

        // The test host has no Loki and no credentials; leaving the sink enabled makes the
        // composition root throw at startup (see LokiEnabledVariable). Set alongside the
        // connection string, before any host is built.
        Environment.SetEnvironmentVariable(LokiEnabledVariable, "false");

        // Redis, through the very same configuration channel production uses (Program.cs:121).
        // Before this the suite had no Redis at all, so every request that touches the cache
        // waited out StackExchange.Redis' 5000 ms AsyncTimeout against localhost:6379 and then
        // threw. Measured on CI: 396 RedisConnectionException events in one run, and 59 discrete
        // stalls of >= 4.5 s accounting for 599 s of a 654 s suite. That was 92% of the wall
        // time of the integration job, and it was not test work.
        //
        // Process-global on purpose, unlike the database: one Redis serves every class, and
        // nothing here needs per-class isolation. The keys written through it are
        // device-signature replay nonces and device challenges, both scoped to a specific device
        // or a random nonce, so two classes cannot collide on one - and a FLUSHALL between
        // classes would be slower than the problem it pretended to solve.
        var redis = await SuiteRedis.Value;
        Environment.SetEnvironmentVariable(RedisConnectionStringVariable, redis.GetConnectionString());
    }

    /// <summary>
    /// Starts the suite's one Redis, configured the way production configures it.
    /// </summary>
    /// <remarks>
    /// The options below are copied from deploy/docker-compose.prod.yml, and not for realism's
    /// sake but because the second one is a security decision the tests are otherwise unable to
    /// exercise: <c>noeviction</c> means a full Redis raises on write instead of silently dropping
    /// a device-signature replay nonce, which is what keeps a signed purchase endpoint
    /// non-replayable. A permissive default here would hide the very property the compose file
    /// deliberately chose. The password is the one thing not carried over - the suite has no
    /// secret to keep, and a throwaway one would only add a string to compare.
    /// </remarks>
    private static async Task<RedisContainer> StartSuiteRedisAsync()
    {
        var container = new RedisBuilder(RedisImage)
            .WithCommand("redis-server",
                "--maxmemory", "128mb",
                "--maxmemory-policy", "noeviction")
            .Build();
        await container.StartAsync();
        return container;
    }

    /// <summary>
    /// Starts the suite's one container, migrates the template database, and freezes it.
    /// </summary>
    private static async Task<PostgreSqlContainer> StartSuiteContainerAsync()
    {
        // Testcontainers 4.x deprecated the parameterless PostgreSqlBuilder() + .WithImage(...) in
        // favour of passing the image to the constructor (CS0618, discussion #1470). Same pinned
        // image, no more obsolete-API warning.
        var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase(TemplateDatabaseName)
            .WithUsername("postgres")
            .WithPassword("postgres")
            // One server for the whole suite instead of one per class means one connection budget
            // for all of them, and PostgreSQL's default of 100 is sized for a single application.
            // ReapPreviousClassesAsync is what actually keeps the count flat; this is the headroom
            // for the window between that sweep and the next class's host finishing its startup
            // migrations, which overlap by design.
            //
            // The flags only, no `postgres` prefix: the image's entrypoint prepends the binary
            // itself when the first argument starts with a dash, and passing it explicitly makes
            // the server read "postgres" as a data directory and exit 1.
            //
            // shared_preload_libraries carries production's setting, so the suite runs against the
            // configuration the deploy actually ships. Without it the QueryStatisticsAreCollected
            // migration installs the extension and nothing else, and the one thing an integration
            // suite can teach us about deployment configuration is lost.
            .WithCommand("-c", "max_connections=300")
            .WithCommand("-c", "shared_preload_libraries=pg_stat_statements")
            .Build();
        await container.StartAsync();

        // The row-level policies name fuelflow_app, and the migration creates them only when that
        // role exists (a development database has no roles). The suite therefore creates the role
        // before the migration, so the policies are installed and can be exercised by SET ROLE.
        await EnsureTenantTestRoleAsync(container);

        // Migrate the template database directly (the app's MigrateDatabaseOnStartup also
        // migrates on host start, but doing it here guarantees readiness, and it has to happen
        // before the database is frozen).
        //
        // Pooling off on purpose: a pooled connection stays open after the context is disposed and
        // would be a live session on the database being frozen. One migration run, so the cost of
        // not pooling is irrelevant.
        await using (var context = new ApplicationDbContext(NonPoolingContextOptions(container, TemplateDatabaseName), TestQrCipher.Instance))
        {
            await context.Database.MigrateAsync();
        }

        await GrantTenantTestRoleAsync(container);

        // Freeze it: is a template, and refuses new connections. From here on nothing can connect
        // to it - which is both why the per-class clones can never race a live session and why a
        // stray connection can no longer make a clone fail.
        //
        // Written straight to pg_database rather than as `ALTER DATABASE ... WITH IS_TEMPLATE`:
        // the ALTER spelling only exists from PostgreSQL 15, the column has been there forever,
        // and this is how every database-cloning tool (pg_basebackup and friends) does it. Issued
        // over the maintenance connection because PostgreSQL refuses to change these flags for a
        // database the current session is using.
        //
        // Parameterised rather than interpolated: ExecuteSqlRaw treats a bare {...} as a
        // placeholder, so a name pasted into the SQL as text fails at run time, not at compile time.
        await using (var maintenance = new ApplicationDbContext(MaintenanceContextOptions(container), TestQrCipher.Instance))
        {
            await maintenance.Database.ExecuteSqlRawAsync(
                "UPDATE pg_database SET datistemplate = true, datallowconn = false WHERE datname = {0}",
                TemplateDatabaseName);
        }

        return container;
    }

    /// <summary>
    /// Drops every connection still open on a database an EARLIER test class used.
    /// </summary>
    /// <remarks>
    /// Sharing one container means sharing one <c>max_connections</c> budget for all 56 classes,
    /// where each class used to get a fresh server with its own 100 slots. Connections outlive
    /// their class: Npgsql keeps idle connections in a pool until its idle-lifetime expires, and
    /// each class now has its own connection string - hence its own pool - so the leftovers add up
    /// instead of sharing one pool. Hosted CI does not sit on a clean boundary either, because a
    /// class that builds a <c>WebApplicationFactory</c> of its own (see
    /// <c>AuthIntegrationTests</c>) disposes it asynchronously.
    ///
    /// The symptom is this, 303 tests into a green run:
    /// <c>Npgsql.PostgresException : 53300: sorry, too many clients already</c>, thrown from
    /// <c>MigrateDatabaseOnStartup</c> while the NEXT class's host boots - a failure in a class
    /// that has nothing to do with connections, pointing at a connection limit nobody set.
    ///
    /// Terminating is always safe here rather than merely usually: the databases being swept are
    /// the throwaway clones of classes that have already finished (the collection disables
    /// parallelisation, so no class is mid-test), and nothing can be connected to the template at
    /// all once it is frozen. Keeping the server-wide count flat is what stops the failure from
    /// coming back as the suite grows.
    /// </remarks>
    private async Task ReapPreviousClassesAsync()
    {
        await using var maintenance = new ApplicationDbContext(MaintenanceContextOptions(DbContainer), TestQrCipher.Instance);
        await maintenance.Database.ExecuteSqlRawAsync(
            """
            SELECT pg_terminate_backend(pid)
              FROM pg_stat_activity
             WHERE datname LIKE 'fuelflow\_it\_%'
               AND pid <> pg_backend_pid()
            """);
    }

    /// <summary>
    /// Clones the frozen template into a fresh, empty database for the calling class and returns its
    /// connection string.
    /// </summary>
    private async Task<string> CloneTemplateDatabaseAsync()
    {
        var name = $"fuelflow_it_{Interlocked.Increment(ref nextDatabaseOrdinal)}";

        // CREATE DATABASE cannot run inside a transaction, and ExecuteSqlRaw issues it as a bare
        // command, so this is fine - but it is why this cannot be a Migrate() call.
        await using (var maintenance = new ApplicationDbContext(MaintenanceContextOptions(DbContainer), TestQrCipher.Instance))
        {
            // EF1002 suppressed rather than satisfied: a database name is an IDENTIFIER, and
            // Postgres will not take an identifier as a bind parameter, so ExecuteSqlAsync cannot
            // express this statement at all. Nothing is interpolated in from outside - both values
            // are built from literals and an ordinal counter a few lines up.
#pragma warning disable EF1002
            await maintenance.Database.ExecuteSqlRawAsync(
                $"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateDatabaseName}\"");
#pragma warning restore EF1002
        }

        // Pooling left at the Npgsql default (on) here, unlike the two maintenance connections:
        // this string is used by every context a test opens, and they are short-lived and numerous.
        var clone = new NpgsqlConnectionStringBuilder(DbContainer.GetConnectionString())
        {
            Database = name,
            Pooling = true,
            SslMode = SslMode.Disable,
        };
        ConnectionString = clone.ConnectionString;

        // The same database, as the two roles production connects as - the test host itself keeps
        // the superuser string, but tests may connect directly as the application role or as the
        // maintenance role to assert what those roles may and may not do.
        var appClone = new NpgsqlConnectionStringBuilder(clone.ConnectionString)
        {
            Username = "fuelflow_app",
            Password = "fuelflow-app-test",
        };
        var maintenanceClone = new NpgsqlConnectionStringBuilder(clone.ConnectionString)
        {
            Username = "fuelflow_maintenance",
            Password = "fuelflow-maintenance-test",
        };
        AppConnectionString = appClone.ConnectionString;
        MaintenanceConnectionString = maintenanceClone.ConnectionString;

        // The host's OWN connection is the application role - the traffic role production runs as,
        // with the grants deploy/database-privileges.tsv states. The suite therefore exercises the
        // real privilege set: a query the matrix does not allow fails here the same way it would
        // fail on the box, instead of passing because the suite connected as the owner.
        //
        // The migration keeps the superuser: it is the only thing that may change the schema, and
        // it runs as the cluster's owner in the template - the same shape as production, where the
        // migrator role owns the tables.
        Environment.SetEnvironmentVariable(ConnectionStringVariable, appClone.ConnectionString);
        Environment.SetEnvironmentVariable(MigrationConnectionStringVariable, clone.ConnectionString);
        Environment.SetEnvironmentVariable(MaintenanceConnectionStringVariable, maintenanceClone.ConnectionString);

        return clone.ConnectionString;
    }

    /// <summary>
    /// Creates the roles the application connects as in production, so the conditional policies
    /// in <c>TheTenantScopeIsAGate</c> are installed in the template. All three are LOGIN roles
    /// with fixed test passwords - the suite connects as the container's superuser and uses
    /// <c>SET ROLE</c> for the policy tests, and other tests connect over TCP as the roles
    /// themselves to assert what they may and may not do.
    /// </summary>
    /// <remarks>
    /// NOREPLICATION and NOBYPASSRLS on purpose: after <c>SET ROLE</c> the policies apply exactly
    /// as they do to that role in production. A superuser may SET ROLE to any role.
    /// </remarks>
    private static async Task EnsureTenantTestRoleAsync(PostgreSqlContainer container)
    {
        await using var maintenance = new ApplicationDbContext(MaintenanceContextOptions(container), TestQrCipher.Instance);
        await maintenance.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fuelflow_app') THEN
                    CREATE ROLE fuelflow_app NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE
                        NOINHERIT NOREPLICATION NOBYPASSRLS;
                END IF;
                ALTER ROLE fuelflow_app LOGIN PASSWORD 'fuelflow-app-test';

                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fuelflow_maintenance') THEN
                    CREATE ROLE fuelflow_maintenance NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE
                        NOINHERIT NOREPLICATION NOBYPASSRLS;
                END IF;
                ALTER ROLE fuelflow_maintenance LOGIN PASSWORD 'fuelflow-maintenance-test';

                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fuelflow_readonly') THEN
                    CREATE ROLE fuelflow_readonly NOLOGIN NOSUPERUSER NOCREATEROLE
                        NOINHERIT NOREPLICATION NOBYPASSRLS;
                END IF;
                ALTER ROLE fuelflow_readonly LOGIN PASSWORD 'fuelflow-readonly-test';
            END
            $$;
            """);
    }

    /// <summary>
    /// The grants <c>deploy/ensure-database-roles.sh</c> gives the traffic and maintenance roles,
    /// read straight out of <c>deploy/database-privileges.tsv</c> - the same file the deploy
    /// script applies - so the template carries exactly the production matrix. Privileges are
    /// revoked first and then granted, so a stale grant cannot survive into the template. The
    /// reporting role is SELECT-only on everything public.
    /// </summary>
    private static async Task GrantTenantTestRoleAsync(PostgreSqlContainer container)
    {
        var matrixPath = LocatePrivilegeMatrix();
        var lines = File.ReadAllLines(matrixPath);

        await using var template = new ApplicationDbContext(NonPoolingContextOptions(container, TemplateDatabaseName), TestQrCipher.Instance);
        await template.Database.ExecuteSqlRawAsync("REVOKE ALL ON SCHEMA public FROM fuelflow_app, fuelflow_maintenance, fuelflow_readonly;");
        await template.Database.ExecuteSqlRawAsync("GRANT USAGE ON SCHEMA public TO fuelflow_app, fuelflow_maintenance, fuelflow_readonly;");

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var parts = line.Split('\t');
            if (parts.Length < 5 || parts[0] != "public") continue;

            var table = parts[1];
            await template.Database.ExecuteSqlRawAsync($"""
                REVOKE ALL ON TABLE public."{table}" FROM fuelflow_app, fuelflow_maintenance;
                {GrantSql("fuelflow_app", parts[2], table)}
                {GrantSql("fuelflow_maintenance", parts[3], table)}
                """);
        }

        await template.Database.ExecuteSqlRawAsync("""
            GRANT SELECT ON ALL TABLES IN SCHEMA public TO fuelflow_readonly;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO fuelflow_app, fuelflow_maintenance, fuelflow_readonly;
            """);

        // Hangfire's own schema. The job queue creates its tables there when it starts, and it
        // starts as the traffic role now - so the schema has to exist and the traffic role has to
        // be allowed to create objects in it. This is the one schema the application may create
        // objects in, and it is the same carve-out production's deploy script makes.
        await template.Database.ExecuteSqlRawAsync("""
            CREATE SCHEMA IF NOT EXISTS hangfire;
            GRANT USAGE, CREATE ON SCHEMA hangfire TO fuelflow_app;
            """);
    }

    static string GrantSql(string role, string priv, string table)
    {
        string? columns = null;
        var basePriv = priv;
        var columnUpdate = priv.IndexOf("+U(", StringComparison.Ordinal);
        if (columnUpdate >= 0)
        {
            basePriv = priv[..columnUpdate];
            var end = priv.IndexOf(')', columnUpdate);
            columns = priv[(columnUpdate + 3)..end];
        }

        var grants = new List<string>();
        if (basePriv.Contains('S')) grants.Add("SELECT");
        if (basePriv.Contains('I')) grants.Add("INSERT");
        if (basePriv.Contains('U') && columns is null) grants.Add("UPDATE");
        if (basePriv.Contains('D')) grants.Add("DELETE");

        var sql = string.Empty;
        if (grants.Count > 0)
            sql += $"GRANT {string.Join(", ", grants)} ON TABLE public.\"{table}\" TO {role};\n";
        if (columns is not null)
            sql += $"GRANT UPDATE ({columns}) ON TABLE public.\"{table}\" TO {role};\n";
        return sql;
    }

    /// <summary>
    /// Finds <c>deploy/database-privileges.tsv</c> by walking up from the test assembly's
    /// directory - the test host runs from <c>bin/Debug/...</c>, so the file is several levels up.
    /// </summary>
    static string LocatePrivilegeMatrix()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && directory is not null; i++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "deploy", "database-privileges.tsv");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("deploy/database-privileges.tsv could not be found from the test assembly");
    }

    /// <summary>
    /// Context options for a throwaway administrative session: no connection pooling, so the
    /// session is gone the moment the context is disposed and it cannot block anything later.
    /// </summary>
    private static DbContextOptions<ApplicationDbContext> NonPoolingContextOptions(PostgreSqlContainer container, string database)
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(container.GetConnectionString())
            {
                Database = database,
                Pooling = false,
                SslMode = SslMode.Disable,
            }.ConnectionString)
            .Options;

    /// <summary>
    /// Context options pointed at the <c>postgres</c> maintenance database, for the statements that
    /// cannot run on the database they act on.
    /// </summary>
    private static DbContextOptions<ApplicationDbContext> MaintenanceContextOptions(PostgreSqlContainer container)
        => NonPoolingContextOptions(container, MaintenanceDatabaseName);

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();

        // The container and the template database are suite-wide and deliberately NOT torn down
        // here: the next test class still needs both. Ryuk reclaims the container when the test
        // host process exits, and the per-class clones die with it.
        //
        // The env vars are likewise left in place rather than cleared. xUnit disposes a class
        // fixture after its class and builds the next one afterwards, so clearing them here would
        // be a no-op at best; leaving the last value set is the safer of the two, because a host
        // that boots slightly later than expected still finds a usable connection string.
    }
}
