using FluentValidation;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Extensions;
using FuelFlow.API.Services;
using FuelFlow.BackgroundJobs;
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

    // Resolved by HangfireMetricsSetup.RegisterHangfireMetricsJobFilter below. Must be
    // registered before the container is built, and must exist in the API because production
    // runs Hangfire in-process here rather than in the JobsWorker.
    builder.Services.AddSingleton<MetricsJobFilter>();
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
            // Every 5 minutes, and it is a SAFETY NET rather than the path. An ORDER_CREATED
            // outbox event is dispatched by the job the outbox interceptor queued when the row
            // was written, which happens within seconds of the purchase; this one only picks up
            // what the fast path missed - an enqueue that failed, or a process that died between
            // the commit and the enqueue.
            //
            // It used to run every minute and be the ONLY path, which meant a voucher could sit
            // unfulfilled for up to a minute and its notification for up to two, and the database
            // grew a Hangfire row every minute to say it had checked and found nothing. Five
            // minutes is the floor that keeps the fallback bounded, and the fallback only runs
            // when something has already gone wrong.
            //
            // The three sweeps this job also runs - the mismatched-fulfilment repair and the two
            // backfills - are repairs rather than user-facing latency, so they are happy at the
            // same cadence.
            "*/5 * * * *");

        recurringJobManager.AddOrUpdate<NotificationService>(
      "process-notifications",
      service => service.ProcessOrderFulfilledEventsAsync(CancellationToken.None),
      // Every 5 minutes, and a safety net - see process-fulfillments. The event itself is
      // dispatched by the job the outbox interceptor queued.
      "*/5 * * * *");

      // #150 W3. A separate job rather than a second call inside the one above: AddOrUpdate takes
      // an expression tree, which cannot hold an async lambda, and one job per event type is what
      // the rest of this schedule does. Same service, same cadence.
      recurringJobManager.AddOrUpdate<NotificationService>(
      "process-worker-notifications",
      service => service.ProcessVoucherIssuedEventsAsync(CancellationToken.None),
      // Every 5 minutes, and a safety net - see process-fulfillments.
      "*/5 * * * *");

        recurringJobManager.AddOrUpdate<RefundStatusSyncService>(
            "sync-refund-status",
            service => service.SyncPendingRefundsAsync(CancellationToken.None),
            // Every 5 minutes rather than every minute. Two reasons, and neither is "it is faster
            // this way". The first half of this job abandons refunds that have been pending for
            // 24 hours, and a refund that is 24 hours old is still 24 hours old five minutes later -
            // the cadence buys nothing there. The second half asks the acquirer what happened to
            // each pending refund, and the acquirer is told about a refund by webhook, so this is
            // only ever the path for a webhook that never arrived; the sibling reconciliation job
            // already argues that a 3-minute grace window makes sub-minute polling moot, and the
            // same argument holds here.
            "*/5 * * * *");

        // Safety net for lost/late Monobank payment webhooks. No-ops unless Monobank:Enabled and
        // ReconciliationEnabled, so it never polls the in-process mock. Every 2 minutes: the outbound
        // Monobank status calls are the cost, and the 3-minute grace window makes sub-minute polling moot.
        recurringJobManager.AddOrUpdate<MonobankReconciliationService>(
            "reconcile-monobank-payments",
            service => service.ReconcilePendingPaymentsAsync(CancellationToken.None),
            "*/2 * * * *");

        // Pulls the acquirer's own statement into monobank_statement_entries, once per merchant per
        // cycle. Hourly rather than every few minutes: it re-reads a rolling window and upserts, so
        // an extra run mostly re-reads rows it already has, and an acquirer does not settle a
        // transaction fast enough for that to buy anything. No-ops unless Monobank:Enabled and
        // StatementImportEnabled.
        recurringJobManager.AddOrUpdate<MonobankStatementImportService>(
            "import-monobank-statements",
            service => service.ImportRecentStatementsAsync(CancellationToken.None),
            "17 * * * *");

        // Proactive low/zero voucher-stock sweep. Every 5 minutes is frequent enough to
        // react to a depleting pool while keeping the grouped count query cheap; the monitor
        // no-ops unless Telegram voucher alerts are enabled, and repeat suppression lives in
        // NotificationDispatcher.
        recurringJobManager.AddOrUpdate<VoucherStockMonitor>(
            "check-voucher-stock",
            service => service.CheckLowStockAsync(CancellationToken.None),
            "*/5 * * * *");

        // Nightly garbage-collection of abandoned orders (soft-deleted + Cancelled, aged past the
        // retention window). No-ops unless an admin has turned DeletedUnpaidOrderCleanup:Enabled on, because the
        // delete is irreversible. Daily at 03:17 UTC — off-peak, and once a day is ample for a
        // backlog that only grows with abandoned checkouts.
        recurringJobManager.AddOrUpdate<DeletedUnpaidOrderCleanupService>(
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

        // Nightly loss booking for operator stock that lapsed unsold: retires in-stock vouchers past
        // their expiration date to Expired so per-batch P&L books their cost as a realised loss instead
        // of phantom future margin. Runs read-only (logs the loss it would book) unless
        // ExpiredVoucherLoss:Enabled is on. Daily at 03:50 UTC — off-peak and staggered after the two GC
        // jobs (03:17, 03:40) so the nightly sweeps never overlap.
        recurringJobManager.AddOrUpdate<ExpiredVoucherLossService>(
            "book-expired-voucher-loss",
            service => service.BookExpiredLossAsync(CancellationToken.None),
            "50 3 * * *");

        // Nightly backstop for Hangfire's own expiration manager, and the alarm that says whether it
        // is still working. The manager does reap finished jobs — measured on production on
        // 2026-10-19, every job row carried an expiry and the table sat at a steady one day of
        // history — but it is a third party's background process, and nothing notices when it stops.
        // This job removes what the manager missed, and logs the table's shape every night.
        //
        // Daily at 04:10 UTC — after the other three nightly sweeps (03:17, 03:40, 03:50) and
        // before the nightly Docker prune on the box (04:20), so the database is not being
        // rewritten in the same minute the images are.
        recurringJobManager.AddOrUpdate<HangfireHistoryCleanupService>(
            "cleanup-hangfire-history",
            service => service.CleanupAsync(CancellationToken.None),
            "10 4 * * *");

        // Daily refresh of OKKO's published pump prices (колонка) into the catalogue. Every price the
        // customer sees — the sale price, the struck "до" price, the per-litre saving — is derived
        // from it, so a stale pump quietly advertises a discount against a price nobody can get.
        // Every 15 minutes: the customer-facing sale price, the struck-through price and the
        // per-litre saving are all derived from the pump price, so the saving advertised to a
        // customer must not point at a price nobody can get. The app already refetches our
        // catalogue whenever it is opened or refocused (usePackages, 15s staleTime) - the
        // customer sees whatever this job last scraped, so freshness belongs HERE and not in
        // the client: a client-side scrape would turn every app open into a request against
        // a third party's site.
        // 96 requests a day is not a meaningful load for one endpoint. OkkoPriceSyncService
        // keeps the last known prices on failure, so a slower cadence is what makes running
        // this often safe - an OKKO outage leaves the catalogue untouched rather than blank.
        // Still staggered off the nightly sweeps at 03:17/03:40/03:50.
        // No-ops unless OkkoPrice:Enabled.
        recurringJobManager.AddOrUpdate<OkkoPriceSyncService>(
            "sync-okko-pump-prices",
            service => service.SyncPricesAsync(CancellationToken.None),
            "*/15 * * * *");

        // Whether a vendor sells a grade we do not carry is a different question from what it
        // charges, and it moves on a different clock: a price changes every day, a new grade is
        // news that arrives a few times a year. Creating one puts a row in the catalogue and a size
        // on the shelf, so it is the weekly job's business - and the weekly job also prices what it
        // created, so a new grade is priced the day it arrives rather than the next quarter.
        recurringJobManager.AddOrUpdate<OkkoPriceSyncService>(
            "sync-okko-catalogue",
            service => service.SyncCatalogueAndPricesAsync(CancellationToken.None),
            "5 3 * * 1");

        // The same cadence for UPG and WOG, which publish a page rather than an API. Their importer
        // creates a grade it has not seen before - with its sizes - and records the price, so a fuel
        // we did not carry appears on the shelf at the vendor's price, marked unavailable until an
        // operator costs it. A failed fetch writes nothing: the last known prices stand.
        recurringJobManager.AddOrUpdate<UpgWogPriceSyncService>(
            "sync-upgwog-pump-prices",
            service => service.SyncPricesAsync(CancellationToken.None),
            "*/15 * * * *");

        // The same split for the two vendors that publish a page: prices every fifteen minutes, the
        // question "do they sell something we do not" once a week.
        recurringJobManager.AddOrUpdate<UpgWogPriceSyncService>(
            "sync-upgwog-catalogue",
            service => service.SyncCatalogueAndPricesAsync(CancellationToken.None),
            "11 3 * * 1");

        // KLO publishes no prices of its own, so Minfin's table is the source: what the network
        // charges, every fifteen minutes, and the grades we do not carry, once a week.
        recurringJobManager.AddOrUpdate<KloPriceSyncService>(
            "sync-klo-prices",
            service => service.SyncPricesAsync(CancellationToken.None),
            "*/15 * * * *");

        recurringJobManager.AddOrUpdate<KloPriceSyncService>(
            "sync-klo-catalogue",
            service => service.SyncCatalogueAndPricesAsync(CancellationToken.None),
            "23 3 * * 1");
    }

    // Production deploys only the API (Hangfire runs in-process, no JobsWorker), and
    // Prometheus scrapes only this process. The voucher-pool observable gauge must
    // therefore be registered here, or fuelflow_vouchers_pool_available_vouchers is
    // never emitted and the VoucherPoolLow alert + business dashboards go silent.
    app.RegisterFuelFlowBusinessGauges();

    // Same trap, one layer over: Hangfire runs in-process here and there is no JobsWorker in
    // production, so without this the MetricsJobFilter is never attached in the only host that
    // runs jobs - fuelflow_jobs_succeeded / fuelflow_jobs_failed are never emitted, and the
    // HangfireJobFailures alert watches an empty series, which reads as "healthy" forever.
    // The filter is resolved from DI, so it must be registered above, before the build.
    app.RegisterHangfireMetricsJobFilter();

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
