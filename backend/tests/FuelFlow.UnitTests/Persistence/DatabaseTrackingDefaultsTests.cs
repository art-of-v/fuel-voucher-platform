using FluentAssertions;
using FuelFlow.API.Extensions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FuelFlow.UnitTests.Persistence;

/// <summary>
/// The application context used to be configured <c>NoTracking</c> globally. That made every
/// read-modify-write in the codebase silently lose its write, while the API still answered with the
/// in-memory values and the audit outbox recorded success. It shipped twice: the supplier endpoints
/// (#929) and the renewal cost reduction (#930), the second of which meant invoicing suppliers for
/// fuel a customer had already paid for.
///
/// These tests read the tracking behaviour off the real DI registration rather than asserting on a
/// copy of it, so the default cannot be quietly reinstated. They need no database: AddDatabase builds
/// an Npgsql data source lazily and nothing here opens a connection.
/// </summary>
public sealed class DatabaseTrackingDefaultsTests
{
    private static (ApplicationDbContext Context, ServiceProvider Provider) BuildContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());
        // A syntactically valid connection string that is never dialled.
        services.AddDatabase("Host=localhost;Database=none;Username=none;Password=none");

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<ApplicationDbContext>(), provider);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
    }

    [Fact]
    public void ApplicationContext_ShouldTrackByDefault()
    {
        var (context, provider) = BuildContext();
        using var _provider = provider;
        using var _context = context;

        context.ChangeTracker.QueryTrackingBehavior
            .Should().Be(QueryTrackingBehavior.TrackAll,
                "a NoTracking default silently drops read-modify-write assignments; see the class remarks");
    }
}