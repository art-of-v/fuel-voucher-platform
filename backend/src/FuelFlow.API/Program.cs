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

    builder.Services.AddSerilog((services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .ConfigureFuelFlowLogging(observability);
    });

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

    ValidateSecurityConfiguration(builder.Configuration, builder.Environment);

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

static void ValidateSecurityConfiguration(
    Microsoft.Extensions.Configuration.IConfiguration configuration,
    IWebHostEnvironment environment)
{
    if (!environment.IsProduction())
        return;

    var monobankEnabled = configuration.GetValue<bool?>("Monobank:Enabled") ?? false;
    var monobankPublicKey = configuration["Monobank:PublicKey"] ?? "";

    if (monobankEnabled &&
        (string.IsNullOrWhiteSpace(monobankPublicKey)
         || monobankPublicKey.Contains("PRODUCTION_PUBLIC_KEY_HERE", StringComparison.OrdinalIgnoreCase)
         || monobankPublicKey.Contains("YOUR_MONOBANK_PUBLIC_KEY", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException(
            "Refusing to start: Monobank is enabled but Monobank:PublicKey is not configured. " +
            "Webhook signature verification cannot be enforced without the real Monobank public key.");
    }

    // Auth:DevBypass is a single switch that turns off most of the auth surface at once:
    // ISmsService becomes FakeSmsService (OTP codes only ever reach the log stream, so anyone
    // who can read logs can log in as anyone), every OTP and global rate limit becomes
    // int.MaxValue, and the Hangfire dashboard drops its authorization filter. One stray
    // environment variable in the deploy config is therefore a full authentication bypass plus
    // an unauthenticated job-management console. It must never be set in Production.
    var devBypass = configuration.GetValue<bool>("Auth:DevBypass");
    if (devBypass)
    {
        throw new InvalidOperationException(
            "Refusing to start: Auth:DevBypass is enabled in Production. This disables real SMS "
            + "delivery (OTP codes go to logs only), removes every rate limit, and unauthenticates "
            + "the Hangfire dashboard. Unset Auth__DevBypass.");
    }

    // OTP codes must actually reach users' phones. With DevBypass off and no
    // SMS provider credentials the app silently falls back to FakeSmsService: codes
    // are only written to logs, nobody can log in, and the failure is easy to
    // miss. Refuse to start instead, like the Monobank guard above.
    var hasSmsClub = ServiceSetup.HasSmsClubConfiguration(configuration);
    if (!hasSmsClub)
    {
        throw new InvalidOperationException(
            "Refusing to start: Auth:DevBypass is off but SMS Club is not configured. " +
            "Set SmsClub__Token and SmsClub__SenderName — " +
            "OTP codes would never be delivered (silent FakeSmsService fallback).");
    }

    // DeviceAuth:Enabled defaults to false, and when it is false DeviceSignatureMiddleware
    // returns before it checks anything at all - device binding on /api/purchases silently
    // does not exist. Shipping that by omission is the failure mode worth blocking.
    //
    // This is NOT a hard refusal, because enabling signature enforcement is coupled to the
    // released mobile build: turning it on before a signing client is in users' hands locks
    // them out of checkout. So the operator has to state the choice in config rather than
    // arrive at it by default.
    var deviceAuthEnabled = configuration.GetValue<bool>("DeviceAuth:Enabled");
    if (!deviceAuthEnabled)
    {
        var acknowledged = configuration.GetValue<bool>("DeviceAuth:AcknowledgeDisabledInProduction");
        if (!acknowledged)
        {
            throw new InvalidOperationException(
                "Refusing to start: DeviceAuth:Enabled is false in Production, so device signature "
                + "verification on the checkout endpoints is inactive. Either set DeviceAuth__Enabled=true "
                + "(only once a signing mobile build is released - enabling it earlier breaks checkout for "
                + "existing installs), or set DeviceAuth__AcknowledgeDisabledInProduction=true to run "
                + "without device binding as a deliberate, recorded decision.");
        }

        Log.Warning(
            "SECURITY: DeviceAuth is disabled in Production by explicit acknowledgement. "
            + "Checkout requests are not device-bound; a stolen access token is sufficient to purchase.");
    }
}
