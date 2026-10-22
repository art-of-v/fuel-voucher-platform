using FuelFlow.API.BackgroundJobs;
using Hangfire;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.Security;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Database;
using Npgsql;

namespace FuelFlow.API.Extensions;

internal static class DatabaseSetup
{
    internal static string BuildConnectionString(this IConfiguration config)
    {
        var connStr = config["Database:ConnectionString"] ?? config["DATABASE_URL"] ?? "";
        if (string.IsNullOrWhiteSpace(connStr))
            return connStr;

        // NOTE: the defaults below are DEVELOPMENT defaults. A production connection string has
        // to say "SSL Mode=Verify Full" and name the CA, because the startup guard refuses to
        // boot otherwise (SecurityConfigurationValidator) - and the reason is the data on this
        // connection: refresh-token hashes, phone numbers, order amounts, voucher ciphertext.
        // "Require" encrypts the session against anyone, and "Trust Server Certificate=true"
        // accepts any certificate at all.
        if (connStr.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
            connStr.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(connStr, UriKind.Absolute, out var uri))
            {
                var host = uri.Host;
                var port = uri.Port == 6543 ? 5432 : uri.Port;
                var database = uri.AbsolutePath.TrimStart('/');
                var userInfo = uri.UserInfo.Split(':', 2);
                var username = Uri.UnescapeDataString(userInfo[0]);
                var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
                return $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
            }
        }
        else
        {
            if (!connStr.Contains("SSL Mode", StringComparison.OrdinalIgnoreCase))
                connStr += ";SSL Mode=Require;Trust Server Certificate=true";
            if (connStr.Contains("Port=6543", StringComparison.OrdinalIgnoreCase))
                connStr = System.Text.RegularExpressions.Regex.Replace(
                    connStr, "Port=6543", "Port=5432", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return connStr;
    }

    internal static IServiceCollection AddDatabase(
        this IServiceCollection services,
        string connectionString,
        int maxPoolSize = DatabaseConnectionPool.DefaultMaxPoolSize)
    {
        // The cap belongs to EVERY pool this process opens, not only to this data source - see
        // DatabaseConnectionPool. The Hangfire storage and the health check get the same string.
        var poolSizeLimited = DatabaseConnectionPool.WithPoolSettings(connectionString, maxPoolSize);

        services.Configure<DatabaseOptions>(opts =>
        {
            opts.ConnectionString = poolSizeLimited;
        });

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(poolSizeLimited);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();
        services.AddSingleton(dataSource);

        services.AddScoped<IImportVouchersDbContext>(brand => brand.GetRequiredService<ApplicationDbContext>());

        services.AddScoped<TenantScopeAccessor>();
        services.AddScoped<TenantConnectionInterceptor>();
        services.AddScoped<TenantTransactionInterceptor>();

        // The outbox dispatcher, registered HERE rather than in the host's Program.cs, so that every
        // host that builds an ApplicationDbContext gets it - a host that forgot it would write
        // outbox rows that nothing dispatches until the safety net catches them, and the gap would
        // look like a slow queue rather than a missing registration.
        //
        // The job client is optional: a host without Hangfire still writes outbox rows, and the
        // safety net in a host that has Hangfire dispatches them.
        services.AddScoped(brand => new OutboxDispatchInterceptor(
            brand.GetService<IBackgroundJobClient>(),
            brand.GetRequiredService<ILogger<OutboxDispatchInterceptor>>()));
        services.AddScoped<OutboxDispatchJob>();

        services.AddDbContext<ApplicationDbContext>((brand, options) =>
        {
            var loggerFactory = brand.GetRequiredService<ILoggerFactory>();
            var environment = brand.GetRequiredService<IHostEnvironment>();

            // The outbox dispatcher. A written outbox row becomes a queued job, so an event is
            // dispatched when it happens rather than when a clock next looks for it. The
            // recurring jobs that used to poll for it every minute stay as a safety net, for the
            // case where the enqueue failed or the process died between the commit and the
            // enqueue - which is why they only claim events old enough not to race with this.
            options.AddInterceptors(
                brand.GetRequiredService<OutboxDispatchInterceptor>(),
                brand.GetRequiredService<TenantConnectionInterceptor>(),
                brand.GetRequiredService<TenantTransactionInterceptor>());
            // Tracking is deliberately left at the default, TrackAll.
            //
            // This context used to be configured NoTracking globally, on the reasonable-sounding grounds
            // that most endpoints only read. The cost was that every read-modify-write silently lost its
            // write: a loaded entity was untracked, property assignments reached no change tracker, and
            // SaveChanges persisted nothing for the row while the outbox rows added alongside it did
            // persist - so the API answered from the in-memory entity, the audit trail recorded success,
            // and the table kept the old value. It shipped twice in a row: the supplier endpoints (#929)
            // and the renewal cost reduction (#930), which meant invoices for fuel a customer had already
            // paid for. A default that can invert correctness is not worth the memory it saves.
            //
            // Read paths that do not need tracking should say so per query with AsNoTracking(), which is
            // explicit and reviewable. DatabaseTrackingDefaultsTests pins this default so it cannot be
            // quietly reinstated.
            options.UseLoggerFactory(loggerFactory)
                   .UseNpgsql(dataSource,
                       b =>
                       {
                           b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);

                           // EF's retrying execution strategy: a transient failure (a dropped
                           // connection, a deadlock, a serialization failure) is retried instead of
                           // failing the request. Three attempts, at most five seconds apart.
                           //
                           // It has one consequence that is not optional: the strategy refuses a
                           // transaction opened by hand, so every explicit transaction in this app
                           // runs inside it, through ReplaySafeTransaction. A transaction opened
                           // outside the strategy throws at its first save - at runtime, on the
                           // money paths, not at compile time.
                           //
                           // The deploy path is retrying too, and that is deliberate: EF's migrator
                           // runs its commands through the same strategy, each migration in its own
                           // transaction, so a blip while the box boots does not need a human to
                           // restart the deploy.
                           b.EnableRetryOnFailure(
                               maxRetryCount: 3,
                               maxRetryDelay: TimeSpan.FromSeconds(5),
                               errorCodesToAdd: null);
                       });

            // Parameter values may contain phone numbers, tokens, and voucher identifiers.
            // They are useful while debugging locally but must never enter production logs.
            if (environment.IsDevelopment())
                options.EnableSensitiveDataLogging();

        });

        return services;
    }

    internal static WebApplication MigrateDatabaseOnStartup(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        try
        {
            var runMigrations = app.Configuration.GetValue<bool?>("RunMigrationsOnBoot") ?? true;

            if (!runMigrations)
            {
                logger.LogInformation("Skipping database migrations (RunMigrationsOnBoot=false).");
                return app;
            }

            logger.LogInformation("Applying pending migrations...");

            // The migration runs as its OWN role, through its own connection, with its own
            // credential - and it is the only thing in the process that ever does. The
            // application's traffic connects as a role that may do the application's work
            // and nothing else (DatabaseRoleValidator), so the schema cannot be changed
            // through the connection the traffic uses.
            //
            // With no migration connection string configured - development and staging,
            // where the one role IS the cluster's own - the application's own connection
            // migrates, exactly as it always has.
            var migrationConnectionString = app.Configuration["Database:MigrationConnectionString"];
            if (string.IsNullOrWhiteSpace(migrationConnectionString))
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                dbContext.Database.Migrate();
            }
            else
            {
                // The cap, like every other pool this process opens - the migration opens
                // one connection and holds it for the length of the migration, and the
                // server's max_connections budget is not a thing one pool may ignore.
                var cappedMigrationConnectionString = DatabaseConnectionPool.WithPoolSettings(migrationConnectionString, DatabaseConnectionPool.ResolveMaxPoolSize(app.Configuration));
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(cappedMigrationConnectionString,
                        b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
                    .Options;

                var cipher = scope.ServiceProvider.GetRequiredService<IQrCipher>();
                using var migrationContext = new ApplicationDbContext(
                    options,
                    cipher,
                    scope.ServiceProvider.GetService<ILogger<ApplicationDbContext>>());
                migrationContext.Database.Migrate();

                // The DDL credential leaves the configuration the moment it has done its
                // work. The application keeps serving on a connection that cannot change
                // the schema, and the process no longer holds the credential that could.
                app.Configuration["Database:MigrationConnectionString"] = null;
            }

            logger.LogInformation("Database migrated successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration failed.");
            throw;
        }

        return app;
    }
}
