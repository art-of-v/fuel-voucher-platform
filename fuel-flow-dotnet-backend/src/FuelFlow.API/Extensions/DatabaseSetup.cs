using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FuelFlow.SharedKernel.Options;

namespace FuelFlow.API.Extensions;

internal static class DatabaseSetup
{
    internal static string BuildConnectionString(this IConfiguration config)
    {
        var connStr = config["Database__ConnectionString"] ?? config["DATABASE_URL"] ?? "";
        if (string.IsNullOrWhiteSpace(connStr))
            return connStr;

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

    internal static IServiceCollection AddDatabase(this IServiceCollection services, string connectionString)
    {
        services.Configure<DatabaseOptions>(opts =>
        {
            opts.ConnectionString = connectionString;
        });

        services.AddScoped<IImportVouchersDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddDbContext<ApplicationDbContext>((provider, options) =>
        {
            var dbOptions = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            options.UseNpgsql(dbOptions.ConnectionString,
                b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));
        });

        return services;
    }

    internal static WebApplication MigrateDatabaseOnStartup(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        try
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var runMigrations = app.Configuration.GetValue<bool?>("RunMigrationsOnBoot")
                ?? (!app.Environment.IsDevelopment());

            if (!runMigrations)
            {
                logger.LogInformation("Skipping database migrations (RunMigrationsOnBoot=false).");
                return app;
            }

            try
            {
                logger.LogInformation("Applying pending migrations...");
                dbContext.Database.Migrate();
                logger.LogInformation("Database migrated successfully.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Migrate() failed, falling back to EnsureCreated()...");
                dbContext.Database.EnsureCreated();
                logger.LogInformation("Database schema created via EnsureCreated().");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration failed.");
            throw;
        }

        return app;
    }
}
