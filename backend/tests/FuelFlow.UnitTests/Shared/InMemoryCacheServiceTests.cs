using FluentAssertions;
using FuelFlow.SharedKernel.Services;
using FuelFlow.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuelFlow.UnitTests.Shared;

public class InMemoryCacheServiceTests
{
    private readonly InMemoryCacheService _service;
    private readonly TestTimeProvider _clock;

    public InMemoryCacheServiceTests()
    {
        _clock = new TestTimeProvider(TestClock.Now);
        _service = new InMemoryCacheService(NullLogger<InMemoryCacheService>.Instance, _clock);
    }

    [Fact]
    public async Task SetAndGet_ShouldRoundTrip()
    {
        const string key = "cache-key";
        const string value = "cache-value";

        await _service.SetAsync(key, value, TimeSpan.FromMinutes(5));

        var result = await _service.GetAsync(key);
        result.Should().Be(value);
    }

    [Fact]
    public async Task Get_ShouldReturnNull_WhenKeyMissing()
    {
        var result = await _service.GetAsync("missing-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task Exists_ShouldBeFalse_AfterRemove()
    {
        const string key = "exists-key";
        await _service.SetAsync(key, "value", TimeSpan.FromMinutes(5));
        (await _service.ExistsAsync(key)).Should().BeTrue();

        await _service.RemoveAsync(key);

        (await _service.ExistsAsync(key)).Should().BeFalse();
    }

    [Fact]
    public async Task Get_ShouldReturnNull_AfterExpiry()
    {
        const string key = "expiring-key";

        await _service.SetAsync(key, "value", TimeSpan.FromMilliseconds(50));

        // The clock moves, not the test: waiting for the TTL to elapse in real time made the
        // test's duration the TTL, and measured the machine's clock instead of the code.
        _clock.Advance(TimeSpan.FromMilliseconds(150));

        var result = await _service.GetAsync(key);
        result.Should().BeNull();
        (await _service.ExistsAsync(key)).Should().BeFalse();
    }

    [Fact]
    public async Task Get_ShouldReturnTheValue_WhileTheEntryHasNotExpired()
    {
        const string key = "live-key";

        await _service.SetAsync(key, "value", TimeSpan.FromMinutes(5));

        _clock.Advance(TimeSpan.FromMinutes(4));

        (await _service.GetAsync(key)).Should().Be("value");
    }

    [Fact]
    public async Task Remove_ShouldDeleteKey()
    {
        const string key = "delete-key";
        await _service.SetAsync(key, "value", TimeSpan.FromMinutes(5));

        await _service.RemoveAsync(key);

        (await _service.GetAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ShouldEvictSoonestExpiringEntry_WhenOverflow()
    {
        for (int i = 0; i < 10_000; i++)
        {
            await _service.SetAsync($"overflow-key-{i}", $"value-{i}", TimeSpan.FromHours(1));
        }

        const string soonestKey = "soonest-key";
        await _service.SetAsync(soonestKey, "value", TimeSpan.FromSeconds(1));

        (await _service.ExistsAsync(soonestKey)).Should().BeFalse();
        (await _service.GetAsync("overflow-key-0")).Should().Be("value-0");
    }
}
