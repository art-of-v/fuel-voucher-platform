using FuelFlow.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace FuelFlow.IntegrationTests;

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
    /// enables the Loki sink, and <c>LoggingSetup.ConfigureFuelFlowLogging</c> fails closed when it
    /// is enabled without credentials (<c>LokiOptions.ValidateCredentials</c>) - correct for a real
    /// deployment, fatal for the test host, which boots under Development but has no Loki to push to
    /// and no credentials. Disabled via the same channel as the connection string above: a process
    /// env var set before the host is built, since <c>Program.cs</c> reads config before
    /// WebApplicationFactory's callbacks run.
    /// </summary>
    private const string LokiEnabledVariable = "Observability__Loki__Enabled";

    public PostgreSqlContainer DbContainer { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("fuelflow_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

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
        await DbContainer.StartAsync();

        // ";SSL Mode=Disable" is mandatory, not cosmetic: BuildConnectionString appends
        // ";SSL Mode=Require;Trust Server Certificate=true" to any string that does not already
        // mention SSL Mode (DatabaseSetup.cs:34-35), and the test container serves no TLS.
        //
        // Set before any host is built. Every integration test class is in the
        // [CollectionDefinition("Integration Tests", DisableParallelization = true)] collection,
        // so mutating process-global state here cannot race another test host.
        Environment.SetEnvironmentVariable(
            ConnectionStringVariable,
            DbContainer.GetConnectionString() + ";SSL Mode=Disable");

        // The test host has no Loki and no credentials; leaving the sink enabled makes the
        // composition root throw at startup (see LokiEnabledVariable). Set alongside the
        // connection string, before any host is built.
        Environment.SetEnvironmentVariable(LokiEnabledVariable, "false");

        // Migrate the container DB directly (the app's MigrateDatabaseOnStartup
        // also migrates on host start, but doing it here guarantees readiness).
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(DbContainer.GetConnectionString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await DbContainer.DisposeAsync();
        Environment.SetEnvironmentVariable(ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(LokiEnabledVariable, null);
    }
}
