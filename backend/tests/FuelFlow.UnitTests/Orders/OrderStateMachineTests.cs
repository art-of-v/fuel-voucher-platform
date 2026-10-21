using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;

namespace FuelFlow.UnitTests.Orders;

public sealed class OrderStateMachineTests
{
    [Theory]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.PendingFulfillment, true)]
    [InlineData(OrderStatus.Paid, OrderStatus.PendingFulfillment, true)]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Paid, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.PendingFulfillment, OrderStatus.Fulfilled, false)]
    [InlineData(OrderStatus.Fulfilled, OrderStatus.PendingFulfillment, false)]
    [InlineData(OrderStatus.Fulfilled, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.PendingFulfillment, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Refunded, OrderStatus.PendingFulfillment, false)]
    [InlineData(OrderStatus.PartiallyFulfilled, OrderStatus.PendingFulfillment, false)]
    public void CanTransition_ShouldReturnExpected(OrderStatus current, OrderStatus target, bool expected)
    {
        OrderStateMachine.CanTransition(current, target).Should().Be(expected);
    }

    [Theory]
    [InlineData(OrderStatus.Fulfilled, true)]
    [InlineData(OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Refunded, true)]
    [InlineData(OrderStatus.PendingPayment, false)]
    [InlineData(OrderStatus.PendingFulfillment, false)]
    public void IsTerminal_ShouldReturnExpected(OrderStatus status, bool expected)
    {
        OrderStateMachine.IsTerminal(status).Should().Be(expected);
    }

    [Theory]
    // The two moves an operator may make by hand: abandon an unpaid order, or hand a paid
    // order to fulfilment.
    [InlineData(OrderStatus.PendingPayment, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.PendingFulfillment, true)]
    [InlineData(OrderStatus.Paid, OrderStatus.PendingFulfillment, true)]
    // A paid order is refunded, not cancelled: cancelling it would keep the money AND the goods.
    [InlineData(OrderStatus.Paid, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.PendingFulfillment, OrderStatus.Cancelled, false)]
    // Terminal orders do not move.
    [InlineData(OrderStatus.Fulfilled, OrderStatus.PendingFulfillment, false)]
    [InlineData(OrderStatus.Fulfilled, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Refunded, OrderStatus.PendingFulfillment, false)]
    [InlineData(OrderStatus.PartiallyRefunded, OrderStatus.PendingFulfillment, false)]
    // And nothing moves backwards.
    [InlineData(OrderStatus.PendingFulfillment, OrderStatus.PendingPayment, false)]
    [InlineData(OrderStatus.PartiallyFulfilled, OrderStatus.PendingFulfillment, false)]
    public void CanOperatorTransition_ShouldReturnExpected(OrderStatus current, OrderStatus target, bool expected)
    {
        OrderStateMachine.CanOperatorTransition(current, target).Should().Be(expected);
    }

    [Theory]
    [InlineData(OrderStatus.PendingFulfillment, true)]
    [InlineData(OrderStatus.PartiallyFulfilled, true)]
    [InlineData(OrderStatus.Fulfilled, false)]
    [InlineData(OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.PendingPayment, false)]
    public void RequiresPayment_ShouldReturnExpected(OrderStatus target, bool expected)
    {
        // The states a fulfilment job hands vouchers for are the states that need the money.
        OrderStateMachine.RequiresPayment(target).Should().Be(expected);
    }

    [Fact]
    public void DescribeRefusal_SaysWhy()
    {
        // The refusal used to be a bare "false": the operator saw "failed to change the status"
        // with the reason only in the server log.
        OrderStateMachine.DescribeRefusal(OrderStatus.Fulfilled, OrderStatus.PendingFulfillment)
            .Should().Contain("Fulfilled").And.Contain("finished");
        OrderStateMachine.DescribeRefusal(OrderStatus.Paid, OrderStatus.Cancelled)
            .Should().Contain("refunded");
        OrderStateMachine.DescribeRefusal(OrderStatus.PendingFulfillment, OrderStatus.PendingPayment)
            .Should().Contain("PendingFulfillment").And.Contain("PendingPayment");
    }
}
