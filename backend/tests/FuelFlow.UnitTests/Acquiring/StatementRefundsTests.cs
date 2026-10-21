using FluentAssertions;
using FuelFlow.Features.Acquiring.Reconciliation;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using Xunit;

namespace FuelFlow.UnitTests.Acquiring;

/// <summary>
/// The act's refunds section: the refunds we issued in the period, reconciled from our side.
/// </summary>
/// <remarks>
/// <para>
/// The rule this file pins is the decision taken on 2026-10-21 (planning #297): Monobank nests a
/// reversal under the payment it reverses and the entry carries no invoice id, so the acquirer's
/// statement alone cannot show money going back. The act therefore reports the refunds we issued,
/// with a figure of their own - never netted into the matched total, because a refund issued in the
/// period can reverse a payment from an earlier one.
/// </para>
/// <para>
/// These are the rules that decide what lands in a document an accountant signs, so they are pinned
/// here rather than only in the handler that feeds them.
/// </para>
/// </remarks>
public class StatementRefundsTests
{
    private static readonly DateTime PeriodStart = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Order Order(Guid id, string invoiceId, DateTime created) => new()
    {
        Id = id,
        UserId = Guid.NewGuid(),
        MonobankInvoiceId = invoiceId,
        Price = 500m,
        Status = OrderStatus.Refunded,
        CreatedAtUtc = created,
        UpdatedAtUtc = created
    };

    private static Payment Capture(Guid id, string invoiceId, DateTime? paidAt) => new()
    {
        Id = id,
        Kind = PaymentKind.Capture,
        Provider = "monobank",
        ProviderInvoiceId = invoiceId,
        AmountUah = 500m,
        AmountKopecks = 50000,
        Currency = "UAH",
        Status = PaymentStatus.Succeeded,
        PaidAtUtc = paidAt,
        CreatedAtUtc = paidAt ?? PeriodStart,
        UpdatedAtUtc = paidAt ?? PeriodStart
    };

    private static Payment Refund(
        Guid id,
        Guid captureId,
        string invoiceId,
        DateTime issuedAt,
        PaymentStatus status = PaymentStatus.Succeeded,
        int amountKopecks = 50000) => new()
    {
        Id = id,
        OrderId = Guid.NewGuid(),
        Kind = PaymentKind.Refund,
        ParentPaymentId = captureId,
        Provider = "monobank",
        ProviderInvoiceId = invoiceId,
        AmountUah = amountKopecks / 100m,
        AmountKopecks = amountKopecks,
        Currency = "UAH",
        Status = status,
        PaidAtUtc = status == PaymentStatus.Succeeded ? issuedAt : null,
        CreatedAtUtc = issuedAt,
        UpdatedAtUtc = issuedAt
    };

    [Fact]
    public void A_Refund_We_Issued_In_The_Period_Is_Reported_With_The_Payment_It_Reverses()
    {
        var capture = Capture(Guid.NewGuid(), "INV-1", new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc));
        var refund = Refund(Guid.NewGuid(), capture.Id, "INV-1", new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc));

        var rows = StatementRefunds.Compose(new[] { refund }, Array.Empty<Order>(), new[] { capture }, PeriodStart, PeriodEnd);

        var row = rows.Should().ContainSingle().Subject;
        row.InvoiceId.Should().Be("INV-1");
        row.Amount.Should().Be(500m);
        row.IssuedAtUtc.Should().Be(new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc));
        // The payment it reverses was taken inside this period, so nothing needs saying about it.
        row.ReversesEarlierPeriodPayment.Should().BeFalse();
        row.Note.Should().BeNull();
    }

    [Fact]
    public void A_Refund_That_Is_Still_In_Flight_Is_Not_An_Issued_Refund()
    {
        // The money has not left yet. The acquirer's statement is what says whether it arrived, and
        // a refund in flight is a request rather than a figure in a signed document.
        var capture = Capture(Guid.NewGuid(), "INV-1", new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc));
        var refund = Refund(
            Guid.NewGuid(), capture.Id, "INV-1",
            new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
            PaymentStatus.Pending);

        var rows = StatementRefunds.Compose(new[] { refund }, Array.Empty<Order>(), new[] { capture }, PeriodStart, PeriodEnd);

        rows.Should().BeEmpty();
    }

    [Fact]
    public void A_Refund_That_Failed_Is_Not_An_Issued_Refund()
    {
        var capture = Capture(Guid.NewGuid(), "INV-1", new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc));
        var refund = Refund(
            Guid.NewGuid(), capture.Id, "INV-1",
            new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
            PaymentStatus.Failed);

        var rows = StatementRefunds.Compose(new[] { refund }, Array.Empty<Order>(), new[] { capture }, PeriodStart, PeriodEnd);

        rows.Should().BeEmpty();
    }

    [Fact]
    public void A_Refund_Issued_Outside_The_Period_Is_Not_This_Period_S_Act()
    {
        var capture = Capture(Guid.NewGuid(), "INV-1", new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc));
        var refund = Refund(Guid.NewGuid(), capture.Id, "INV-1", new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc));

        var rows = StatementRefunds.Compose(new[] { refund }, Array.Empty<Order>(), new[] { capture }, PeriodStart, PeriodEnd);

        rows.Should().BeEmpty();
    }

    [Fact]
    public void A_Refund_That_Reverses_An_Earlier_Period_S_Payment_Says_So()
    {
        // The case that makes a net figure meaningless: the money went back in October, but it came
        // in in September. The two sides of the act are not talking about the same money, so the row
        // says so rather than leaving the reader to work it out from the dates.
        var capture = Capture(Guid.NewGuid(), "INV-1", new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc));
        var refund = Refund(Guid.NewGuid(), capture.Id, "INV-1", new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc));

        var rows = StatementRefunds.Compose(new[] { refund }, Array.Empty<Order>(), new[] { capture }, PeriodStart, PeriodEnd);

        var row = rows.Should().ContainSingle().Subject;
        row.ReversesEarlierPeriodPayment.Should().BeTrue();
        row.ReversedPaymentAtUtc.Should().Be(new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc));
        row.Note.Should().Contain("2026-09-20");
    }

    [Fact]
    public void A_Refund_Without_A_Capture_Falls_Back_To_The_Order_It_Belongs_To()
    {
        // A capture we cannot find leaves the order as the only evidence of when the money came in.
        var order = Order(Guid.NewGuid(), "INV-1", new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc));
        var refund = Refund(Guid.NewGuid(), Guid.NewGuid(), "INV-1", new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc));
        refund.OrderId = order.Id;

        var rows = StatementRefunds.Compose(new[] { refund }, new[] { order }, Array.Empty<Payment>(), PeriodStart, PeriodEnd);

        var row = rows.Should().ContainSingle().Subject;
        row.ReversedPaymentAtUtc.Should().Be(order.CreatedAtUtc);
        row.ReversesEarlierPeriodPayment.Should().BeTrue();
    }

    [Fact]
    public void The_Period_Boundaries_Are_Half_Open()
    {
        // The same bounds the statement rows use: the act is [from, to), so the month tiles with
        // its neighbour instead of overlapping it.
        var capture = Capture(Guid.NewGuid(), "INV-1", new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc));

        var atStart = Refund(Guid.NewGuid(), capture.Id, "INV-1", PeriodStart);
        var atEnd = Refund(Guid.NewGuid(), capture.Id, "INV-2", PeriodEnd);

        var rows = StatementRefunds.Compose(
            new[] { atStart, atEnd }, Array.Empty<Order>(), new[] { capture }, PeriodStart, PeriodEnd);

        rows.Should().ContainSingle();
        rows[0].IssuedAtUtc.Should().Be(PeriodStart);
    }

    [Fact]
    public void The_Refunds_Are_Ordered_By_When_They_Went_Back()
    {
        var capture = Capture(Guid.NewGuid(), "INV-1", new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc));
        var later = Refund(Guid.NewGuid(), capture.Id, "INV-2", new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
        var earlier = Refund(Guid.NewGuid(), capture.Id, "INV-1", new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc));

        var rows = StatementRefunds.Compose(
            new[] { later, earlier }, Array.Empty<Order>(), new[] { capture }, PeriodStart, PeriodEnd);

        rows.Select(r => r.IssuedAtUtc).Should().BeInAscendingOrder();
    }
}
