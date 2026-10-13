using FuelFlow.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;
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
        Environment.SetEnvironmentVariable(ConnectionStringVariable, ConnectionString);

        // The test host has no Loki and no credentials; leaving the sink enabled makes the
        // composition root throw at startup (see LokiEnabledVariable). Set alongside the
        // connection string, before any host is built.
        Environment.SetEnvironmentVariable(LokiEnabledVariable, "false");
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
            .Build();
        await container.StartAsync();

        // Migrate the template database directly (the app's MigrateDatabaseOnStartup also
        // migrates on host start, but doing it here guarantees readiness, and it has to happen
        // before the database is frozen).
        //
        // Pooling off on purpose: a pooled connection stays open after the context is disposed and
        // would be a live session on the database being frozen. One migration run, so the cost of
        // not pooling is irrelevant.
        await using (var context = new ApplicationDbContext(NonPoolingContextOptions(container, TemplateDatabaseName)))
        {
            await context.Database.MigrateAsync();
        }

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
        await using (var maintenance = new ApplicationDbContext(MaintenanceContextOptions(container)))
        {
            await maintenance.Database.ExecuteSqlRawAsync(
                "UPDATE pg_database SET datistemplate = true, datallowconn = false WHERE datname = {0}",
                TemplateDatabaseName);
        }

        return container;
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
        await using (var maintenance = new ApplicationDbContext(MaintenanceContextOptions(DbContainer)))
        {
            await maintenance.Database.ExecuteSqlRawAsync(
                $"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateDatabaseName}\"");
        }

        // Pooling left at the Npgsql default (on) here, unlike the two maintenance connections:
        // this string is used by every context a test opens, and they are short-lived and numerous.
        return new NpgsqlConnectionStringBuilder(DbContainer.GetConnectionString())
        {
            Database = name,
            SslMode = SslMode.Disable,
        }.ConnectionString;
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