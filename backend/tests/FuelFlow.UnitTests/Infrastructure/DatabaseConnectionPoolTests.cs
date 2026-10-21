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
    public void WithPoolSettings_ShouldCapAStringThatHasNoCap()
    {
        var capped = DatabaseConnectionPool.WithPoolSettings(ConnectionString, 7);

        capped.Should().Be($"{ConnectionString};Maximum Pool Size=7;No Reset On Close=true");
    }

    [Fact]
    public void WithPoolSettings_ShouldKeepAnExplicitCapInTheString()
    {
        var withCap = $"{ConnectionString};Maximum Pool Size=42";

        DatabaseConnectionPool.WithPoolSettings(withCap, 7)
            .Should().Be($"{withCap};No Reset On Close=true");
    }

    [Fact]
    public void WithPoolSettings_ShouldIgnoreANonPositiveCap()
    {
        // The cap is ignored; the reset policy is not - it is not a cap, and the string it
        // applies to is still the string the pools are opened with.
        DatabaseConnectionPool.WithPoolSettings(ConnectionString, 0)
            .Should().Be($"{ConnectionString};No Reset On Close=true");
        DatabaseConnectionPool.WithPoolSettings(ConnectionString, -1)
            .Should().Be($"{ConnectionString};No Reset On Close=true");
    }

    [Fact]
    public void WithPoolSettings_ShouldNotDoubleTheSeparator()
    {
        DatabaseConnectionPool.WithPoolSettings(ConnectionString + ";", 5)
            .Should().Be($"{ConnectionString};Maximum Pool Size=5;No Reset On Close=true");
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

        options.ConnectionString.Should().Be($"{ConnectionString};Maximum Pool Size=9;No Reset On Close=true");
    }

    /// <summary>
    /// The reset policy is the second thing this method applies, and it is the one with a cost:
    /// a connection returned to the pool keeps whatever the last user left on it. This
    /// application leaves nothing behind, and that is what makes the setting safe here.
    /// </summary>
    [Fact]
    public void WithPoolSettings_ShouldTurnTheResetOff()
    {
        DatabaseConnectionPool.WithPoolSettings(ConnectionString, 5)
            .Should().Contain("No Reset On Close=true");
    }

    [Fact]
    public void WithPoolSettings_ShouldKeepAnExplicitResetSettingInTheString()
    {
        // The operator's string is the one that knows: an explicit setting wins, exactly as an
        // explicit cap does.
        var withReset = $"{ConnectionString};No Reset On Close=false";

        DatabaseConnectionPool.WithPoolSettings(withReset, 5)
            .Should().Be($"{withReset};Maximum Pool Size=5");
    }

    /// <summary>
    /// The Hangfire storage's pool keeps the reset. The storage sets a session variable of its
    /// own and LISTENs, and Npgsql's reset is what stops that state from reaching the next user
    /// of the same physical connection.
    /// </summary>
    [Fact]
    public void WithMaxPoolSize_ShouldNotTouchTheReset()
    {
        var capped = DatabaseConnectionPool.WithMaxPoolSize(ConnectionString, 7);

        capped.Should().Be($"{ConnectionString};Maximum Pool Size=7");
        capped.Should().NotContain("No Reset On Close");
    }

}
