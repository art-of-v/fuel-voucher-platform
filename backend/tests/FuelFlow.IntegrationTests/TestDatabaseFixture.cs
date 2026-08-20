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
    public PostgreSqlContainer DbContainer { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("fuelflow_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development env (like AuthIntegrationTests/VoucherImportIntegrationTests)
        // so startup-time config reads (AddJwtAuth, AddDatabase) resolve the
        // development settings instead of throwing on a missing Jwt:Secret.
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();

            // Add test database context
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(DbContainer.GetConnectionString());
            });
        });
    }

    public async Task InitializeAsync()
    {
        await DbContainer.StartAsync();

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
        await DbContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}
