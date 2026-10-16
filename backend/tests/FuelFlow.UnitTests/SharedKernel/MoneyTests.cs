using FuelFlow.SharedKernel;
using FluentAssertions;
using Xunit;

namespace FuelFlow.UnitTests.SharedKernel;

/// <summary>
/// The two conversions in <see cref="Money"/> that both end at a <c>long</c>, and take the same
/// argument type.
/// </summary>
/// <remarks>
/// These exist as separate tests because the mistake of using the wrong one costs a factor of 100 and
/// nothing catches it but a value assertion: routing an already-kopeck refund through
/// <see cref="Money.ForMonobank"/> (which takes UAH) multiplies it, and the refund request looks
/// entirely plausible until Monobank rejects it or, worse, someone does not notice. That is not
/// hypothetical - it is what happened while this type was being introduced, and
/// <c>RefundOrderCommandHandlerTests</c> caught it on the first run.
/// <para>
/// The names are the guard. A single <c>ToLong</c> would have had no such problem and would also have
/// had no way to tell a reader which unit the caller was handing over.
/// </para>
/// </remarks>
public class MoneyTests
{
    [Fact]
    public void ToKopecks_RoundsHalfAwayFromZero()
    {
        // The domain stores 2 dp, so 456.70 must map to exactly 45670 - a value that differs only by
        // representation must not become a different amount.
        Money.ToKopecks(456.70m).Should().Be(45670m);
        Money.ToKopecks(0.01m).Should().Be(1m);
    }

    [Fact]
    public void ToKopecks_RoundsHalfAwayFromZero_RatherThanBankers()
    {
        // Banker's rounding would send 0.125 UAH to 12 and 0.135 to 14. Checkout already rounds
        // half away from zero, and this conversion has to agree with it or the stored kopeck image of
        // a domain amount drifts by a step.
        Money.ToKopecks(0.125m).Should().Be(13m);
        Money.ToKopecks(0.135m).Should().Be(14m);
    }

    [Fact]
    public void ForMonobank_TakesUah_AndProducesKopecks()
    {
        Money.ForMonobank(2500m).Should().Be(250_000L);
    }

    [Fact]
    public void KopecksForWire_TakesKopecks_AndDoesNotScaleThem()
    {
        // The whole point of having two methods. 250_000 kopecks is 2,500 UAH and must cross the wire
        // as 250_000 - if this ever returned 25_000_000 the refund would be a hundred times its size.
        Money.KopecksForWire(250_000m).Should().Be(250_000L);
    }

    [Fact]
    public void ForMonobank_And_KopecksForWire_Disagree_ByDesign_OnTheSameInput()
    {
        // Written as a test on purpose. It looks like a tautology and it is the opposite: it fails the
        // day someone collapses the two methods into one, which is the only way this bug can come back.
        Money.ForMonobank(2500m).Should().Be(250_000L);
        Money.KopecksForWire(2500m).Should().Be(2_500L);
    }

    [Fact]
    public void FromKopecks_IsExact_AndRoundTrips()
    {
        Money.FromKopecks(45670m).Should().Be(456.70m);
        Money.FromKopecks(Money.ToKopecks(978.57m)).Should().Be(978.57m);
    }
}
