using FluentValidation;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Extensions;
using FuelFlow.API.Services;
using FuelFlow.Features.ErrorLogs.Logging;
using FuelFlow.Middleware;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Serilog;
using StackExchange.Redis;
using System.Text.RegularExpressions;

try
{
    Log.Logger = new LoggerConfiguration()
        .WriteTo.Console()
        .CreateBootstrapLogger();
}
catch (InvalidOperationException ex) when (ex.Message.Contains("already frozen", StringComparison.OrdinalIgnoreCase))
{
}

try
{
    // Render nodes share the kernel inotify instance limit (128). The default
    // reloadOnChange watchers on appsettings*.json make startup crash with
    // "user limit on the number of inotify instances has been reached" when the
    // limit is exhausted (observed in Render deploy logs, exit 139). Config
    // files never change inside the container, so disable reload via the
    // generic-host setting read by ApplyDefaultAppConfiguration. Note: the
    // previously attempted AppContext switch
    // "Microsoft.Extensions.Configuration.Json.DisableReloadOnChange" does not
    // exist in .NET 10 and had no effect.
    Environment.SetEnvironmentVariable("DOTNET_hostbuilder__reloadConfigOnChange", "false");

    var builder = WebApplication.CreateBuilder(args);

    var observability = builder.Configuration
        .GetSection(ObservabilityOptions.SectionName)
        .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

    // Sentry is opt-in: with no DSN the SDK is never initialised, so the app has no
    // dependency on Sentry and sends nothing. Same shape as the OTLP/Loki exporters.
    if (observability.Sentry.IsConfigured)
    {
        builder.WebHost.UseSentry(options =>
        {
            options.Dsn = observability.Sentry.Dsn;
            // Reuse the one environment label the rest of observability already uses,
            // so a single Sentry project can serve multiple environments unambiguously.
            options.Environment = observability.Environment;
            options.Release = typeof(Program).Assembly.GetName().Version?.ToString();

            // --- Privacy: this API carries phone numbers and, critically, voucher QR
            // payloads (bearer instruments that cannot be revoked once leaked). None of
            // that may ever reach a third party, so request bodies and PII are hard-off.
            options.SendDefaultPii = false;
            options.MaxRequestBodySize = Sentry.Extensibility.RequestSize.None;

            // Errors are always captured; performance tracing is sampled (default 0 =
            // off) so trace volume never quietly burns the free-tier event quota.
            options.TracesSampleRate = observability.Sentry.TracesSampleRatio;
        });
    }

    // writeToProviders: true keeps the other registered ILoggerProviders in the logging
    // pipeline alongside Serilog's own sinks (Console/Loki). Without it Serilog owns logging
    // exclusively and the DatabaseLoggerProvider registered below never receives any events,
    // so nothing is ever written to error_logs and the admin Error Logs screen stays empty.
    builder.Services.AddSerilog((services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .ConfigureFuelFlowLogging(observability);
    }, writeToProviders: true);

    builder.Services.AddSingleton<ILoggerProvider, DatabaseLoggerProvider>();

    builder.Services.AddFuelFlowObservability(builder.Configuration);

    var connectionString = builder.Configuration.BuildConnectionString();
    builder.Services
        .AddDatabase(connectionString)
        .AddJwtAuth(builder.Configuration, builder.Environment)
        .AddDefaultAuthorizationPolicy()
        .AddFeatureServices(builder.Configuration)
        .AddCorsPolicy(builder.Configuration)
        .AddForwardedHeadersSupport(builder.Configuration)
        .AddRateLimiting()
        .AddSwaggerDocs();

    builder.Services.AddScoped<ProductOwnerBootstrap>();

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
    var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    var redisConfig = RedisConnectionParser.Parse(redisConnection);
    var redisSanitized = redisConfig.Contains('@')
        ? redisConfig[..redisConfig.IndexOf('@')] + "@<redacted>"
        : Regex.Replace(redisConfig, "(?<=password=)[^,]+", "<redacted>", RegexOptions.IgnoreCase);
    Log.Information("Redis connection: {Redis}", redisSanitized);

    // One multiplexer for the whole process. The cache consumes it via
    // ConnectionMultiplexerFactory and /health pings the same instance, so the
    // health check reports the state of the connection the app actually uses.
    // AbortOnConnectFail=false keeps a Redis outage from killing the process at
    // boot: the multiplexer retries in the background and commands fail per
    // request instead, which is what the /health endpoint is meant to report.
    var redisOptions = ConfigurationOptions.Parse(redisConfig);
    redisOptions.AbortOnConnectFail = false;
    var redisMultiplexer = ConnectionMultiplexer.Connect(redisOptions);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);
    builder.Services.AddStackExchangeRedisCache(options =>
        options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redisMultiplexer));
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();
    builder.Services.AddResponseCaching();
    builder.Services.Configure<FormOptions>(options =>
        options.MultipartBodyLengthLimit = 25_000_000);
    builder.WebHost.ConfigureKestrel(options =>
        options.Limits.MaxRequestBodySize = 25_000_000);
    builder.Services.AddControllers(options =>
        {
            options.Filters.Add<FuelFlow.Middleware.ValidateAttribute>();
        })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

    var app = builder.Build();

    SecurityConfigurationValidator.Validate(builder.Configuration, builder.Environment);

    app.UseSwaggerDocs();
    app.UseAppPipeline();

    var allowDashboardBypass = builder.Configuration.GetValue<bool>("Auth:DevBypass");
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireDashboardAuthorizationFilter(allowDashboardBypass) },
        IgnoreAntiforgeryToken = true
    });
    app.MigrateDatabaseOnStartup();

    using (var scope = app.Services.CreateScope())
    {
        var bootstrap = scope.ServiceProvider.GetRequiredService<ProductOwnerBootstrap>();
        await bootstrap.ExecuteAsync();
    }

    using (var scope = app.Services.CreateScope())
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

        recurringJobManager.AddOrUpdate<RefundStatusSyncService>(
            "sync-refund-status",
            service => service.SyncPendingRefundsAsync(CancellationToken.None),
            "*/1 * * * *");

        // Safety net for lost/late Monobank payment webhooks. No-ops unless Monobank:Enabled and
        // ReconciliationEnabled, so it never polls the in-process mock. Every 2 minutes: the outbound
        // Monobank status calls are the cost, and the 3-minute grace window makes sub-minute polling moot.
        recurringJobManager.AddOrUpdate<MonobankReconciliationService>(
            "reconcile-monobank-payments",
            service => service.ReconcilePendingPaymentsAsync(CancellationToken.None),
            "*/2 * * * *");

        // Proactive low/zero voucher-stock sweep. Every 5 minutes is frequent enough to
        // react to a depleting pool while keeping the grouped count query cheap; the monitor
        // no-ops unless Telegram voucher alerts are enabled, and repeat suppression lives in
        // NotificationDispatcher.
        recurringJobManager.AddOrUpdate<VoucherStockMonitor>(
            "check-voucher-stock",
            service => service.CheckLowStockAsync(CancellationToken.None),
            "*/5 * * * *");

        // Nightly garbage-collection of abandoned orders (soft-deleted + Cancelled, aged past the
        // retention window). No-ops unless an admin has turned OrderCleanup:Enabled on, because the
        // delete is irreversible. Daily at 03:17 UTC — off-peak, and once a day is ample for a
        // backlog that only grows with abandoned checkouts.
        recurringJobManager.AddOrUpdate<OrderCleanupService>(
            "cleanup-abandoned-orders",
            service => service.CleanupAbandonedOrdersAsync(CancellationToken.None),
            "17 3 * * *");

        // Nightly retention sweep for high-churn operational tables (spent OTPs, dead refresh
        // tokens, processed outbox events, read notifications, aged error logs, stale push tokens).
        // Runs read-only (counts only) unless DataRetention:Enabled is on. Daily at 03:40 UTC —
        // off-peak and staggered after order cleanup (03:17) so the two GC jobs never overlap.
        recurringJobManager.AddOrUpdate<DataRetentionService>(
            "data-retention-cleanup",
            service => service.CleanupAsync(CancellationToken.None),
            "40 3 * * *");
    }

    // Production deploys only the API (Hangfire runs in-process, no JobsWorker), and
    // Prometheus scrapes only this process. The voucher-pool observable gauge must
    // therefore be registered here, or fuelflow_vouchers_pool_available_vouchers is
    // never emitted and the VoucherPoolLow alert + business dashboards go silent.
    app.RegisterFuelFlowBusinessGauges();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    try
    {
        Log.CloseAndFlush();
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("already frozen", StringComparison.OrdinalIgnoreCase))
    {
    }
}
