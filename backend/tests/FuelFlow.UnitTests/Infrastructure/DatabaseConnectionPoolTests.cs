using FluentAssertions;
using FuelFlow.API.Extensions;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FuelFlow.UnitTests.Infrastructure;

/// <summary>
/// The connection-pool cap. A process opens MORE THAN ONE pool against Postgres - the
/// application's data source, the Hangfire storage, the health check - and Npgsql's default cap is
/// 100 per pool. The application capped only its own data source, so the two pools that can
/// actually storm the server during an incident were uncapped, while the compose file's
/// "the application's pool is capped at 5, so this is insurance against a connection storm" was
/// true of one pool out of four.
///
/// These pin the behaviour that changed: the cap is one value, it goes on every pool, and an
/// explicit cap in the connection string still wins.
/// </summary>
public sealed class DatabaseConnectionPoolTests
{
    private const string ConnectionString = "Host=postgres;Database=fuelflow;Username=fuelflow";

    [Fact]
    public void WithMaxPoolSize_ShouldCapAStringThatHasNoCap()
    {
        var capped = DatabaseConnectionPool.WithMaxPoolSize(ConnectionString, 7);

        capped.Should().Be($"{ConnectionString};Maximum Pool Size=7");
    }

    [Fact]
    public void WithMaxPoolSize_ShouldKeepAnExplicitCapInTheString()
    {
        var withCap = $"{ConnectionString};Maximum Pool Size=42";

        DatabaseConnectionPool.WithMaxPoolSize(withCap, 7).Should().Be(withCap);
    }

    [Fact]
    public void WithMaxPoolSize_ShouldIgnoreANonPositiveCap()
    {
        DatabaseConnectionPool.WithMaxPoolSize(ConnectionString, 0).Should().Be(ConnectionString);
        DatabaseConnectionPool.WithMaxPoolSize(ConnectionString, -1).Should().Be(ConnectionString);
    }

    [Fact]
    public void WithMaxPoolSize_ShouldNotDoubleTheSeparator()
    {
        DatabaseConnectionPool.WithMaxPoolSize(ConnectionString + ";", 5)
            .Should().Be($"{ConnectionString};Maximum Pool Size=5");
    }

    [Fact]
    public void ResolveMaxPoolSize_ShouldFallBackToTheDefault()
    {
        var configuration = new ConfigurationBuilder().Build();

        DatabaseConnectionPool.ResolveMaxPoolSize(configuration)
            .Should().Be(DatabaseConnectionPool.DefaultMaxPoolSize);
    }

    [Fact]
    public void ResolveMaxPoolSize_ShouldReadTheConfiguredValue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnectionPool.MaxPoolSizeConfigurationKey] = "12"
            })
            .Build();

        DatabaseConnectionPool.ResolveMaxPoolSize(configuration).Should().Be(12);
    }

    [Fact]
    public void ResolveMaxPoolSize_ShouldIgnoreANonPositiveConfiguredValue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnectionPool.MaxPoolSizeConfigurationKey] = "0"
            })
            .Build();

        DatabaseConnectionPool.ResolveMaxPoolSize(configuration)
            .Should().Be(DatabaseConnectionPool.DefaultMaxPoolSize);
    }

    [Fact]
    public void AddDatabase_ShouldRegisterTheCappedConnectionString()
    {
        var services = new ServiceCollection();

        services.AddDatabase(ConnectionString, 9);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        options.ConnectionString.Should().Be($"{ConnectionString};Maximum Pool Size=9");
    }
}
