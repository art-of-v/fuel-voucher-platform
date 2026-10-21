using FluentAssertions;
using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Orders.SharedModels;

namespace FuelFlow.UnitTests.Monobank;

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
}
