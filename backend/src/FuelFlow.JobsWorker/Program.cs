// Every background-job type this worker runs lives in the API, and it is referenced through an
// alias so this worker schedules the SAME implementations against the shared Hangfire storage.
// A local copy used to live here and had silently diverged (its expiry filter was commented out,
// it had no renewal or auto-refund handling), and because both processes registered the recurring
// job under the same id, whichever started last silently won.
//
// NotificationService was the last of them: the copy that sat here had no push sender, no
// safety-net claim window and no voucher-issued path, so this worker wrote notifications the
// API did not - and skipped the ones the API did. One home per type is the rule; the API is it.
using FulfillmentService = FuelFlow.API.BackgroundJobs.FulfillmentService;
using NotificationService = FuelFlow.API.BackgroundJobs.NotificationService;
using RefundStatusSyncService = FuelFlow.API.BackgroundJobs.RefundStatusSyncService;
using VoucherStockMonitor = FuelFlow.API.BackgroundJobs.VoucherStockMonitor;
using FuelFlow.API.Extensions;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Notifications.Push;
using FuelFlow.Features.Settings;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Resilience;
using FuelFlow.SharedKernel.Time;
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

    // The application's clock, as a service - the same registration the API makes, so the worker
    // and the API read the same time. See SystemClock.
    builder.Services.AddSingleton<TimeProvider>(SystemClock.Provider);

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

    // One cap for every pool in this process - the worker's own data source, the Hangfire storage
    // and the Postgres health check. The worker shares the database with the API, so an uncapped
    // pool here eats the server's max_connections budget without the API's cap noticing.
    var maxPoolSize = DatabaseConnectionPool.ResolveMaxPoolSize(builder.Configuration);
    var cappedConnectionString = DatabaseConnectionPool.WithPoolSettings(connectionString!, maxPoolSize);

    // The Hangfire storage gets the cap and NOT the reset: it sets a session variable of its
    // own and LISTENs, and that state belongs to a library whose SQL is not ours to read.
    var storageConnectionString = DatabaseConnectionPool.WithMaxPoolSize(connectionString!, maxPoolSize);

    builder.Services.AddHealthChecks()
        .AddNpgSql(cappedConnectionString, name: "postgres", tags: ["ready"]);

    // The same database registration the API uses, rather than a second AddDbContext: this host
    // runs the API's services, so it must also get the outbox interceptor and - now that the
    // tables carry row-level policies - the tenant scope. A worker has no HTTP request, so the
    // accessor resolves to the system scope and the background jobs keep reading across tenants.
    builder.Services.AddDatabase(connectionString!, maxPoolSize,
        builder.Configuration["Database:MaintenanceConnectionString"],
        requireMaintenanceConnection: builder.Environment.IsProduction());

    builder.Services.AddScoped<NotificationService>();
    builder.Services.AddScoped<VoucherStockMonitor>();

    // The notification service this worker runs is the API's, and it sends the push as well as
    // writing the in-app row - so the sender the API registers has to be registered here too.
    // The push is best-effort by contract: a failure is logged and swallowed, and the master
    // switch (ExpoPush:Enabled) turns it off without a code change.
    //
    // The deadline and the policy are the API's, because the client is the API's: this worker
    // runs the API's notification service, and a push server that never answers must not hold
    // the worker's thread either.
    builder.Services.Configure<ExpoPushOptions>(
        builder.Configuration.GetSection(ExpoPushOptions.SectionName));
    var expoPush = builder.Configuration
        .GetSection(ExpoPushOptions.SectionName)
        .Get<ExpoPushOptions>();
    builder.Services.AddOutboundHttpClient<IExpoPushSender, ExpoPushClient>(
        OutboundHttpClient.TimeoutOrDefault(expoPush?.TimeoutSeconds ?? ExpoPushOptions.DefaultTimeoutSeconds),
        OutboundCallKind.Write);

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
        // The acquirer, with the deadline the API gives it and the same decision not to retry
        // it: the invoice creation is a POST, and repeating that is how one basket becomes two
        // invoices. The worker only ever reads the acquirer (the refund status sync), but it
        // shares the client with the API's write path, so the declaration is the conservative one.
        builder.Services.AddOutboundHttpClient(
            $"monobank-{MonobankMerchant.Live}",
            TimeSpan.FromSeconds(MonobankOptions.ClientTimeoutSeconds),
            OutboundCallKind.Write);
        builder.Services.AddOutboundHttpClient(
            $"monobank-{MonobankMerchant.Sandbox}",
            TimeSpan.FromSeconds(MonobankOptions.ClientTimeoutSeconds),
            OutboundCallKind.Write);
    }

    builder.Services.AddScoped<RefundStatusSyncService>();

    // Dependencies of the API's FulfillmentService. It normally gets these from the API's
    // ServiceSetup, which this worker does not call, so they are registered explicitly -
    // otherwise the "process-fulfillments" job fails activation here.
    builder.Services.AddScoped<AuditEventService>();
    builder.Services.AddScoped<RefundOrderCommandHandler>();
    builder.Services.AddScoped<RuntimeSettingsService>();

    builder.Services.AddHangfire(configuration => configuration
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(c =>
            c.UseNpgsqlConnection(storageConnectionString)));

    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = 1;
        options.Queues = new[] { "default" };
    });

    // Measures every job centrally, so jobs added later are instrumented automatically.
    // The type itself lives in the shared kernel because the API runs Hangfire in-process in
    // production and must attach the filter too - see MetricsJobFilter.
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

    host.RegisterHangfireMetricsJobFilter();

    // Observable gauge: polled by the exporter on scrape rather than pushed, so the
    // query must stay cheap and must never throw or it would break the whole scrape.
    // Shared with the API (which is the only scraped process in production) so the two
    // registrations cannot drift — see BusinessGaugeSetup.
    host.RegisterFuelFlowBusinessGauges();

    using (var scope = host.Services.CreateScope())
    {
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        // The API owns the recurring-job registry: it reconciles it on boot, removing any id this
        // code no longer registers. The three ids below are a SUBSET of the API's list, so the API's
        // reconciliation leaves them alone - and the reverse would not hold, so the worker does NOT
        // reconcile. It must not, until its schedule is the same set as the API's rather than a
        // subset of it.
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