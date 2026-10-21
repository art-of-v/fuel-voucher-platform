using System.Runtime.CompilerServices;
using FuelFlow.SharedKernel.Time;

namespace FuelFlow.Testing;

/// <summary>
/// The clock the test suite runs on: one fixed instant, shared by the three test projects.
/// </summary>
/// <remarks>
/// <para>
/// A test that reads the wall clock can fail at midnight, at the end of a month, or across a DST
/// boundary - and the failure looks like a product bug. That was planning finding C-10, measured
/// over 189 files. The suite now reads a fixed instant instead, and the production code under test
/// reads the same instant through <see cref="SystemClock"/>.
/// </para>
/// <para>
/// The instant is a Thursday at midday UTC, deliberately: midnight is the boundary that breaks
/// date arithmetic, and the middle of the day leaves the month-end and month-start paths a whole
/// day away from the edge.
/// </para>
/// <para>
/// One copy, compiled into all three test projects, so the three suites cannot drift apart on what
/// "now" means. The file lives here rather than in a project of its own because there is no fourth
/// assembly to maintain for thirty lines.
/// </para>
/// </remarks>
public static class TestClock
{
    /// <summary>The instant every test reads as "now".</summary>
    public static DateTime UtcNow { get; } = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>The same instant, with a zero offset.</summary>
    public static DateTimeOffset Now { get; } = new(UtcNow, TimeSpan.Zero);

    /// <summary>A clock that always returns <see cref="UtcNow"/>.</summary>
    public static TimeProvider Provider { get; } = new TestTimeProvider(Now);

    /// <summary>
    /// A run-unique string of decimal digits, for the places a test needs a value that differs from
    /// every other test - a phone number above all, which the database holds unique.
    /// </summary>
    /// <remarks>
    /// It used to come from the clock (<c>DateTime.UtcNow.Ticks</c>), which is precisely what
    /// finding C-10 took away: on a fixed clock every test would mint the same number, and the
    /// second test to insert it would fail on the unique index. The uniqueness has to come from
    /// somewhere that is not the clock.
    /// </remarks>
    public static string UniqueDigits(int count)
    {
        var limit = 1L;
        for (var i = 0; i < count; i++)
        {
            limit *= 10;
        }

        return Random.Shared.NextInt64(limit).ToString($"D{count}");
    }
}

/// <summary>
/// A clock the test can move.
/// </summary>
/// <remarks>
/// <para>
/// The BCL's own <c>FakeTimeProvider</c> lives in a package this repository does not take a
/// dependency on for a handful of lines; this is the whole of it - <see cref="TimeProvider"/> with
/// one method to override, plus a way to move the instant forward.
/// </para>
/// <para>
/// The advance is what a test that waits for time to pass needs. A test that slept for the TTL to
/// expire was a test whose duration was the TTL: slow, and it measured the machine's clock rather
/// than the code. A test that advances a clock it owns is both faster and exact.
/// </para>
/// </remarks>
public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset instant;

    public TestTimeProvider(DateTimeOffset instant) => this.instant = instant;

    public override DateTimeOffset GetUtcNow() => instant;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by) => instant = instant.Add(by);
}

/// <summary>
/// Puts the whole test assembly on the fixed clock before any test in it runs.
/// </summary>
/// <remarks>
/// A module initializer, because the clock has to be in place before the first test reads it, and
/// an assembly fixture would be too late for the tests that run first. It runs once per assembly,
/// so the unit suites, the brand suite and the integration suite each install their own copy of the
/// same fixed instant.
/// </remarks>
internal static class TestClockModule
{
    [ModuleInitializer]
    internal static void Install() => SystemClock.Provider = TestClock.Provider;
}
