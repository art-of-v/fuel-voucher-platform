using FuelFlow.JobsWorker.Jobs;
using FuelFlow.JobsWorker.Services;
using FuelFlow.Options;
using FuelFlow.Persistence;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Starting FuelFlow JobsWorker");

    // Set environment to Development by default if not specified
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")))
    {
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
    }

    var builder = Host.CreateApplicationBuilder(args);

    // Configure Serilog
    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    // Configure Database Options from appsettings
    builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
    var connectionString = builder.Configuration.GetSection("Database")["ConnectionString"];

    // Add DbContext
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(
            connectionString));

    // Register services
    builder.Services.AddScoped<FulfillmentService>();

    // Configure Hangfire
    builder.Services.AddHangfire(configuration => configuration
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(c =>
            c.UseNpgsqlConnection(connectionString)));

    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = 1;
        options.Queues = new[] { "default" };
    });

    var host = builder.Build();

    // Apply migrations
    using (var scope = host.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Log.Information("Applying database migrations...");
        dbContext.Database.Migrate();
        Log.Information("Database migrations applied successfully");
    }

    // Schedule recurring jobs using dependency injection
    using (var scope = host.Services.CreateScope())
    {
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        recurringJobManager.AddOrUpdate<FulfillmentService>(
            "process-fulfillments",
            service => service.ProcessPendingOrdersAsync(CancellationToken.None),
            "*/1 * * * *"); // Every minute
        Log.Information("Recurring jobs configured");
    }

    Log.Information("FuelFlow JobsWorker started successfully - Hangfire Dashboard available at /hangfire");

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}