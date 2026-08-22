using FluentValidation;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Extensions;
using FuelFlow.Features.ErrorLogs.Logging;
using FuelFlow.Middleware;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Serilog;

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

    builder.Services.AddSerilog((services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services.AddSingleton<ILoggerProvider, DatabaseLoggerProvider>();

    var connectionString = builder.Configuration.BuildConnectionString();
    builder.Services
        .AddDatabase(connectionString)
        .AddJwtAuth(builder.Configuration, builder.Environment)
        .AddFeatureServices(builder.Configuration)
        .AddCorsPolicy(builder.Configuration)
        .AddForwardedHeadersSupport(builder.Configuration)
        .AddRateLimiting()
        .AddSwaggerDocs();

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
    var redisConfig = ParseRedisConnection(redisConnection);
    var redisSanitized = redisConfig.Contains('@')
        ? redisConfig[..redisConfig.IndexOf('@')] + "@<redacted>"
        : redisConfig;
    Log.Information("Redis connection: {Redis}", redisSanitized);
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConfig);
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

static string ParseRedisConnection(string connection)
{
    if (!connection.Contains("://"))
        return connection;

    var uri = new Uri(connection);
    var host = uri.Host;
    var port = uri.Port > 0 ? uri.Port : 6379;
    var password = uri.UserInfo?.Contains(':') == true
        ? uri.UserInfo.Split(':', 2)[1]
        : uri.UserInfo ?? "";
    var ssl = uri.Scheme.StartsWith("rediss", StringComparison.OrdinalIgnoreCase);

    var parts = new List<string> { $"{host}:{port}" };
    if (!string.IsNullOrEmpty(password))
        parts.Add($"password={password}");
    if (ssl)
        parts.Add("ssl=True");

    return string.Join(",", parts);
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

    // OTP codes must actually reach users' phones. With DevBypass off and no
    // Twilio credentials the app silently falls back to FakeSmsService: codes
    // are only written to logs, nobody can log in, and the failure is easy to
    // miss. Refuse to start instead, like the Monobank guard above.
    var devBypass = configuration.GetValue<bool>("Auth:DevBypass");
    if (!devBypass && !ServiceSetup.HasTwilioConfiguration(configuration))
    {
        throw new InvalidOperationException(
            "Refusing to start: Auth:DevBypass is off but Twilio is not configured. " +
            "OTP codes would never be delivered (silent FakeSmsService fallback).");
    }
}
