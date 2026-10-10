using FuelFlow.JobsWorker.Observability;
using FuelFlow.JobsWorker.Services;
// Aliased rather than imported: the API defines its own NotificationService, so a plain
// using would make that name ambiguous here.
// FulfillmentService, RefundStatusSyncService and VoucherStockMonitor live ONLY in the API and
// are referenced via alias so this worker schedules the same implementations against the shared
// Hangfire storage. A local copy used to live here and had silently diverged (its expiry filter
// was commented out, it had no renewal or auto-refund handling), and because both processes
// registered the recurring job under the same id, whichever started last silently won.
using FulfillmentService = FuelFlow.API.BackgroundJobs.FulfillmentService;
using RefundStatusSyncService = FuelFlow.API.BackgroundJobs.RefundStatusSyncService;
using VoucherStockMonitor = FuelFlow.API.BackgroundJobs.VoucherStockMonitor;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Settings;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Persistence;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting FuelFlow JobsWorker");

    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")))
    {
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
    }

    // A minimal web host (rather than a plain generic host) so the worker can expose
    // a Prometheus scrape endpoint and health probes on its own dedicated port.
    var builder = WebApplication.CreateBuilder(args);

    var observability = builder.Configuration
        .GetSection(ObservabilityOptions.SectionName)
        .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

    builder.WebHost.UseUrls($"http://0.0.0.0:{observability.Prometheus.WorkerPort}");

    builder.Services.AddSerilog((services, lc) =>
    {
        lc
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .ConfigureFuelFlowLogging(observability);
    });

    builder.Services.Configure<ObservabilityOptions>(
        builder.Configuration.GetSection(ObservabilityOptions.SectionName));
    builder.Services.Configure<TelegramOptions>(
        builder.Configuration.GetSection(TelegramOptions.SectionName));
    builder.Services.AddFuelFlowObservability(builder.Configuration);
    builder.Services.AddFuelFlowAlerting(builder.Configuration);

    builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
    var connectionString = builder.Configuration.GetSection("Database")["ConnectionString"];

    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString!, name: "postgres", tags: ["ready"]);

    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(
            connectionString));

    builder.Services.AddScoped<NotificationService>();
    builder.Services.AddScoped<VoucherStockMonitor>();

    // The API and this worker share one Hangfire storage and both listen on the
    // "default" queue, so the worker can pick up jobs the API enqueued. That includes
    // RefundStatusSyncService, which needs IMonobankClientFactory - without this
    // registration the job fails activation and retries ten times. The factory also
    // hands out the in-process mock while Monobank:Enabled is false, so no separate
    // mock registration is needed here.
    builder.Services.Configure<MonobankOptions>(
        builder.Configuration.GetSection(MonobankOptions.SectionName));

    var monobankOptions = builder.Configuration
        .GetSection(MonobankOptions.SectionName)
        .Get<MonobankOptions>();

    builder.Services.AddSingleton<IMonobankClientFactory, MonobankClientFactory>();

    if (monobankOptions?.Enabled == true)
    {
        builder.Services.AddHttpClient($"monobank-{MonobankMerchant.Live}");
        builder.Services.AddHttpClient($"monobank-{MonobankMerchant.Sandbox}");
    }

    builder.Services.AddScoped<RefundStatusSyncService>();

    // Dependencies of the API's FulfillmentService. It normally gets these from the API's
    // ServiceSetup, which this worker does not call, so they are registered explicitly -
    // otherwise the "process-fulfillments" job fails activation here.
    builder.Services.AddScoped<ProviderEventService>();
    builder.Services.AddScoped<RefundOrderCommandHandler>();
    builder.Services.AddScoped<RuntimeSettingsService>();

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

    // Measures every job centrally, so jobs added later are instrumented automatically.
    builder.Services.AddSingleton<MetricsJobFilter>();

    var host = builder.Build();

    if (observability.Prometheus.Enabled)
    {
        host.MapPrometheusScrapingEndpoint();
    }

    host.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false
    });
    host.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

    using (var scope = host.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Log.Information("Applying database migrations...");
        dbContext.Database.Migrate();
        Log.Information("Database migrations applied successfully");
    }

    GlobalJobFilters.Filters.Add(host.Services.GetRequiredService<MetricsJobFilter>());

    // Observable gauge: polled by the exporter on scrape rather than pushed, so the
    // query must stay cheap and must never throw or it would break the whole scrape.
    // Shared with the API (which is the only scraped process in production) so the two
    // registrations cannot drift — see BusinessGaugeSetup.
    host.RegisterFuelFlowBusinessGauges();

    using (var scope = host.Services.CreateScope())
    {
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        recurringJobManager.AddOrUpdate<FulfillmentService>(
            "process-fulfillments",
            service => service.ProcessPendingOrdersAsync(CancellationToken.None),
            "*/1 * * * *");
        recurringJobManager.AddOrUpdate<NotificationService>(
            "process-notifications",
            service => service.ProcessOrderFulfilledEventsAsync(CancellationToken.None),
            "*/1 * * * *");
        // Every 5 minutes is frequent enough to react to a depleting pool while keeping
        // the grouped count query cheap; repeat suppression lives in the monitor itself.
        recurringJobManager.AddOrUpdate<VoucherStockMonitor>(
            "check-voucher-stock",
            service => service.CheckLowStockAsync(CancellationToken.None),
            "*/5 * * * *");
        Log.Information("Recurring jobs configured");
    }

    Log.Information(
        "FuelFlow JobsWorker started successfully - metrics and health on port {Port}",
        observability.Prometheus.WorkerPort);

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