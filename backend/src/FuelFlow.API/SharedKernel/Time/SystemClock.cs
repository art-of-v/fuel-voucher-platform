namespace FuelFlow.SharedKernel.Time;

/// <summary>
/// The application's clock. Every production read of "now" goes through here.
/// </summary>
/// <remarks>
/// <para>
/// The clock is a <see cref="TimeProvider"/> - the BCL's seam for exactly this - held here rather
/// than injected into every class, for two reasons that are both about reach:
/// </para>
/// <list type="bullet">
/// <item>
/// The clock has to reach code the container never builds: EF Core value converters, entity
/// factories, static helpers, and the records a background job hands to another one. Constructor
/// injection covers the classes DI builds and nothing else, so a clock that only lived there would
/// leave a third of the reads on the wall clock - and a test that cannot control those reads is a
/// test that fails at midnight.
/// </item>
/// <item>
/// The clock has to reach code a test builds by hand. The suite constructs handlers directly
/// (<c>new RefundOrderCommandHandler(...)</c>) far more often than it resolves them, and a required
/// constructor parameter would mean editing every one of those call sites to say what time it is -
/// 500-odd edits that say nothing.
/// </item>
/// </list>
/// <para>
/// The holder is therefore the single source of truth, and the container is fed from it: the
/// composition root registers <c>SystemClock.Provider</c> as the <see cref="TimeProvider"/> service,
/// so a class that prefers constructor injection gets the very same clock the static reads see.
/// </para>
/// <para>
/// In production the provider is <see cref="TimeProvider.System"/> and nothing changes: the clock
/// reads what it always read. In a test the provider is a fixed instant, so the suite's result does
/// not depend on when it runs - which is the whole point of the seam (planning finding C-10).
/// </para>
/// </remarks>
public static class SystemClock
{
    private static TimeProvider provider = TimeProvider.System;

    /// <summary>
    /// The clock the application reads. Defaults to the system clock.
    /// </summary>
    /// <remarks>
    /// Settable, because a test installs a fixed clock here. Production never assigns it: the
    /// composition root reads it and registers it with the container.
    /// </remarks>
    public static TimeProvider Provider
    {
        get => provider;
        set => provider = value;
    }

    /// <summary>
    /// The current UTC time, as the clock sees it. The replacement for <c>DateTime.UtcNow</c>.
    /// </summary>
    public static DateTime UtcNow => provider.GetUtcNow().UtcDateTime;

    /// <summary>
    /// The current instant, as the clock sees it, with a zero offset. For the places that carry an
    /// offset rather than a bare UTC timestamp - a signature timestamp, a message header.
    /// </summary>
    public static DateTimeOffset Now => provider.GetUtcNow();
}
