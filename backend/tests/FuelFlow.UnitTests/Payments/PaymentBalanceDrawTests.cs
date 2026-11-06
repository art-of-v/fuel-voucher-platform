using FluentAssertions;
using FuelFlow.Payments.SharedModels;
using Xunit;
using static FuelFlow.Payments.SharedModels.PaymentBalance;

namespace FuelFlow.UnitTests.Payments;

/// <summary>
/// The arithmetic of spending a balance: which transfers a wanted amount comes out of, and what it
/// says when they do not cover it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PaymentBalance.DrawAgainst"/> is the only part of the held-money rule that can be tested
/// without a database, and it is the part where a wrong answer costs money: a planner that rounds up
/// draws more than a transfer holds, and one that stops at the first payment leaves an order unpaid
/// with the money for it sitting in the next row. The refusals those produce are the database's, and
/// the tests for them are in the integration suite; what belongs here is that the plan this function
/// returns is always a legal one.
/// </para>
/// <para>
/// The rows are hand-built rather than read, because the order they arrive in is the caller's promise —
/// <c>AvailableAsync</c> returns oldest first — and this function's whole rule is that it does not
/// reorder what it is given.
/// </para>
/// </remarks>
public sealed class PaymentBalanceDrawTests
{
    private static readonly Guid First = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Second = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Third = new("33333333-3333-3333-3333-333333333333");

    private static Held Money(Guid id, decimal remaining) => new(id, remaining, DateTime.UnixEpoch);

    [Fact]
    public void One_transfer_that_is_exactly_enough_is_drawn_in_full()
    {
        var plan = PaymentBalance.DrawAgainst([Money(First, 900m)], 900m);

        plan.Covers.Should().BeTrue();
        plan.Draws.Should().HaveCount(1);
        plan.Draws[0].Should().Be(new Draw(First, 900m));
    }

    [Fact]
    public void The_oldest_transfer_is_emptied_before_the_next_is_touched()
    {
        var plan = PaymentBalance.DrawAgainst([Money(First, 400m), Money(Second, 500m), Money(Third, 600m)], 900m);

        plan.Covers.Should().BeTrue();
        plan.Draws.Should().HaveCount(2,
            "400 out of the first and 500 out of the second, and the third is not in this sale at all");
        plan.Draws[0].Should().Be(new Draw(First, 400m));
        plan.Draws[1].Should().Be(new Draw(Second, 500m));
    }

    [Fact]
    public void A_transfer_bigger_than_the_order_is_never_split_across_the_remainder()
    {
        var plan = PaymentBalance.DrawAgainst([Money(First, 400m), Money(Second, 500m)], 350m);

        plan.Covers.Should().BeTrue();
        plan.Draws.Should().HaveCount(1,
            "the order takes what it owes and no more; a draw of the whole transfer would be a sale "
            + "paying for fuel it never sold");
        plan.Draws[0].Should().Be(new Draw(First, 350m));
    }

    [Fact]
    public void An_amount_of_nothing_is_covered_by_drawing_nothing()
    {
        var plan = PaymentBalance.DrawAgainst([Money(First, 900m)], 0m);

        plan.Covers.Should().BeTrue("an order that owes nothing needs no money, which is a different fact "
            + "from an order that has none");
        plan.Draws.Should().BeEmpty();
    }

    [Fact]
    public void A_negative_amount_is_an_error_and_not_a_refund()
    {
        var act = () => PaymentBalance.DrawAgainst([Money(First, 900m)], -1m);

        // A negative here would come back as "covers, with no draws", and the caller would settle an
        // order after writing nothing — which is the one outcome this table exists to make impossible.
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void An_empty_balance_is_short_by_the_whole_amount()
    {
        var plan = PaymentBalance.DrawAgainst([], 900m);

        plan.Covers.Should().BeFalse();
        plan.ShortfallUah.Should().Be(900m);
        plan.Draws.Should().BeEmpty();
    }

    [Fact]
    public void A_shortfall_names_what_is_missing_and_not_something_else()
    {
        var plan = PaymentBalance.DrawAgainst([Money(First, 400m), Money(Second, 300m)], 900m);

        plan.Covers.Should().BeFalse();
        plan.ShortfallUah.Should().Be(200m, "the sentence the customer is given quotes this figure");
    }

    [Fact]
    public void A_spent_transfer_in_the_list_is_skipped_rather_than_drawn_on()
    {
        var plan = PaymentBalance.DrawAgainst([Money(First, 0m), Money(Second, 900m)], 900m);

        plan.Covers.Should().BeTrue();
        plan.Draws.Should().HaveCount(1,
            "a zero row is not money, and writing a draw of 0 is refused by the table anyway");
        plan.Draws[0].Should().Be(new Draw(Second, 900m));
    }

    [Fact]
    public void Every_plan_draws_no_more_than_the_balance_holds()
    {
        var held = new[] { Money(First, 123.45m), Money(Second, 67.89m), Money(Third, 0.16m) };
        var free = held.Sum(h => h.RemainingUah);

        foreach (var wanted in new[] { 0.01m, 50m, 123.45m, 191.34m, 191.50m, 500m })
        {
            var plan = PaymentBalance.DrawAgainst(held, wanted);
            var drawn = plan.Draws.Sum(d => d.AmountUah);

            drawn.Should().BeLessThanOrEqualTo(free);
            (drawn + plan.ShortfallUah).Should().Be(wanted,
                "the plan either pays the amount or says exactly how far it is short, and never both short "
                + "of the two");
        }
    }
}
