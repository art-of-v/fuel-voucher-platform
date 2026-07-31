using FluentValidation;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Extensions;
using FuelFlow.Middleware;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
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
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    var connectionString = builder.Configuration.BuildConnectionString();
    builder.Services
        .AddDatabase(connectionString)
        .AddJwtAuth(builder.Configuration, builder.Environment)
        .AddFeatureServices(builder.Configuration)
        .AddCorsPolicy(builder.Configuration)
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
    builder.Services.AddControllers(options =>
        {
            options.Filters.Add<FuelFlow.Middleware.ValidateAttribute>();
        })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

    var app = builder.Build();

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
