using System;
using FluentAssertions;
using FuelFlow.Features.Orders;
using FuelFlow.Features.Orders.Receipt;
using FuelFlow.Features.Orders.SharedModels;
using Xunit;

namespace FuelFlow.UnitTests.Orders;

public sealed class OrderReceiptRefundSectionTests
{
    private static readonly DateTime When = new(2026, 10, 11, 13, 45, 0, DateTimeKind.Utc);
    private static readonly Guid OrderId = Guid.Parse("9f5bebdf-9f99-45f1-86ec-876f8840b9aa");

    [Theory]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.PendingFulfillment)]
    [InlineData(OrderStatus.PartiallyFulfilled)]
    [InlineData(OrderStatus.Fulfilled)]
    public void A_Payment_Receipt_Never_Carries_A_Refund_Section(OrderStatus status)
    {
        // Regression: the section used to be gated on "an amount is refundable", and a paid order
        // whose vouchers were all delivered has 100% of its value refundable. Every customer's own
        // receipt therefore printed "Повернення коштів" with the full total - which reads as "you were
        // charged, and then charged back", the opposite of what happened. Found on a real 1 UAH
        // purchase on the live merchant, 2026-10-11.
        OrderReceiptController.BuildRefundSection(status, 100, When, OrderId).Should().BeNull();
    }

    [Theory]
    [InlineData(OrderStatus.PartiallyRefunded)]
    [InlineData(OrderStatus.Refunded)]
    public void A_Refunded_Order_Prints_The_Amount_It_Got_Back(OrderStatus status)
    {
        var section = OrderReceiptController.BuildRefundSection(status, 250, When, OrderId);

        section.Should().NotBeNull();
        section!.Amount.Should().Be(2.50m);        // kopecks -> hryvnia
        section.RefundedAtUtc.Should().Be(When);
        section.OriginalOrderId.Should().Be(OrderId);
    }

    [Theory]
    [InlineData(OrderStatus.Refunded)]
    [InlineData(OrderStatus.PartiallyRefunded)]
    public void A_Refunded_State_With_Nothing_Left_To_Return_Prints_No_Figure(OrderStatus status)
    {
        // The state can be reached with the money already out and nothing left unfulfilled. Printing
        // "0.00 грн повернення" would be a figure nobody reconciled against.
        OrderReceiptController.BuildRefundSection(status, 0, When, OrderId).Should().BeNull();
    }
}