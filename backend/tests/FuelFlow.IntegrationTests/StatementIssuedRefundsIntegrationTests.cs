using FluentAssertions;
using FuelFlow.Features.Acquiring;
using FuelFlow.Features.Acquiring.Reconciliation;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The act's refunds section against a real PostgreSQL: the refunds we issued in the period, with
/// the payment each of them gives back against.
/// </summary>
/// <remarks>
/// <para>
/// The decision (planning #297, 2026-10-21): Monobank nests a reversal under the payment it
/// reverses and the entry carries no invoice id, so the acquirer's statement alone cannot show
/// money going back. The act reconciles the refunds from our side instead - and keeps the figure
/// separate from the matched total, because a refund issued in the period can reverse a payment
/// from an earlier one.
/// </para>
/// <para>
/// Real PostgreSQL because the period filter and the two lookups behind it are queries; an
/// in-memory brand would answer a different question about them.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class StatementIssuedRefundsIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public StatementIssuedRefundsIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly DateTime PeriodStart = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private ApplicationDbContext Context()
    {
        var scope = _fixture.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    /// <summary>
    /// The fixture is shared across the assembly, so without this the refunds one test seeds are
    /// visible to the next - and a test asserting "nothing was refunded" fails on someone else's row.
    /// </summary>
    private async Task ResetAsync()
    {
        await using var context = _fixture.CreateOwnerContext();
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "monobank_statement_entries" RESTART IDENTITY CASCADE""");
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments" RESTART IDENTITY CASCADE""");
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "orders" RESTART IDENTITY CASCADE""");
    }

    private async Task<Guid> EnsureUserAsync(ApplicationDbContext context)
    {
        var id = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = id,
            PhoneNumber = $"+38{id:N}"[..20],
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static Order Order(Guid userId, string invoiceId, DateTime created) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Price = 500m,
        Status = OrderStatus.Refunded,
        MonobankInvoiceId = invoiceId,
        MonobankMerchant = MonobankMerchant.Live,
        MonobankStatus = MonobankStatus.Success,
        CreatedAtUtc = created,
        UpdatedAtUtc = created
    };

    private static Payment Capture(Guid orderId, string invoiceId, DateTime paidAt) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Kind = PaymentKind.Capture,
        Provider = "monobank",
        Merchant = MonobankMerchant.Live,
        ProviderInvoiceId = invoiceId,
        AmountUah = 500m,
        AmountKopecks = 50000,
        Currency = "UAH",
        Status = PaymentStatus.Succeeded,
        PaidAtUtc = paidAt,
        CreatedAtUtc = paidAt,
        UpdatedAtUtc = paidAt
    };

    private static Payment Refund(
        Guid orderId,
        Guid captureId,
        string invoiceId,
        DateTime createdAt,
        DateTime? paidAt,
        PaymentStatus status) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Kind = PaymentKind.Refund,
        ParentPaymentId = captureId,
        Provider = "monobank",
        Merchant = MonobankMerchant.Live,
        ProviderInvoiceId = invoiceId,
        AmountUah = 500m,
        AmountKopecks = 50000,
        Currency = "UAH",
        Status = status,
        // The database refuses a succeeded payment with no paid-at, so a pending refund carries
        // none - exactly as the real code writes it.
        PaidAtUtc = paidAt,
        CreatedAtUtc = createdAt,
        UpdatedAtUtc = createdAt
    };

    private static MonobankStatementEntry Entry(string statementId, string invoiceId, long kopecks) => new()
    {
        Id = Guid.NewGuid(),
        Merchant = MonobankMerchant.Live,
        StatementId = statementId,
        InvoiceId = invoiceId,
        Reference = "FuelFlow Order demo",
        AmountKopecks = kopecks,
        Ccy = 980,
        Status = "settled",
        AcquirerCreatedAtUtc = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc),
        FirstSeenAtUtc = TestClock.UtcNow,
        LastSeenAtUtc = TestClock.UtcNow
    };

    private async Task<StatementReconciliationResponse> ActAsync()
    {
        var handler = new GetStatementReconciliationQueryHandler(Context());
        return await handler.HandleAsync(new StatementReconciliationQuery
        {
            Merchant = MonobankMerchant.Live,
            FromUtc = PeriodStart,
            ToUtc = PeriodEnd,
        });
    }

    [Fact]
    public async Task A_Refund_We_Issued_In_The_Period_Is_Reported_With_The_Payment_It_Reverses()
    {
        await ResetAsync();
        await using var setup = Context();
        var userId = await EnsureUserAsync(setup);

        var order = Order(userId, "INV-1", new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc));
        var capture = Capture(order.Id, "INV-1", new DateTime(2026, 10, 3, 9, 1, 0, DateTimeKind.Utc));
        var refund = Refund(
            order.Id, capture.Id, "INV-1",
            new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
            PaymentStatus.Succeeded);

        setup.Orders.Add(order);
        setup.Payments.AddRange(capture, refund);
        setup.MonobankStatementEntries.Add(Entry("ST-1", "INV-1", 50000));
        await setup.SaveChangesAsync();

        var act = await ActAsync();

        var row = act.RefundsIssued.Should().ContainSingle().Subject;
        row.RefundId.Should().Be(refund.Id);
        row.InvoiceId.Should().Be("INV-1");
        row.OrderId.Should().Be(order.Id);
        row.Amount.Should().Be(500m);
        row.IssuedAtUtc.Should().Be(new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc));
        row.ReversedPaymentAtUtc.Should().Be(new DateTime(2026, 10, 3, 9, 1, 0, DateTimeKind.Utc));
        row.ReversesEarlierPeriodPayment.Should().BeFalse();

        act.Summary.IssuedRefundCount.Should().Be(1);
        act.Summary.IssuedRefundTotalUah.Should().Be(500m);
    }

    [Fact]
    public async Task The_Matched_Total_Stays_Gross_And_Never_Nets_The_Refunds_Out_Of_It()
    {
        await ResetAsync();
        await using var setup = Context();
        var userId = await EnsureUserAsync(setup);

        var order = Order(userId, "INV-1", new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc));
        var capture = Capture(order.Id, "INV-1", new DateTime(2026, 10, 3, 9, 1, 0, DateTimeKind.Utc));
        var refund = Refund(
            order.Id, capture.Id, "INV-1",
            new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
            PaymentStatus.Succeeded);

        setup.Orders.Add(order);
        setup.Payments.AddRange(capture, refund);
        setup.MonobankStatementEntries.Add(Entry("ST-1", "INV-1", 50000));
        await setup.SaveChangesAsync();

        var act = await ActAsync();

        // The statement row is the capture: it carries the invoice we refunded, and what the
        // acquirer filed is a capture - money in - with the reversal hanging off it. So the row is
        // matched against the order, and the acquirer's gross for it is in the matched total
        // instead of in no total at all.
        act.Rows.Should().ContainSingle(r => r.Status == StatementMatchStatus.Matched);
        act.Rows.Should().ContainSingle(r => r.LocalAmount == 500m);

        // The refunds figure sits beside the matched total and does not reduce it. Nobody has to
        // subtract two numbers whose periods may differ.
        act.Summary.IssuedRefundTotalUah.Should().Be(500m);
        act.Summary.MatchedTotalUah.Should().Be(500m);
        act.Summary.UnmatchedTotalUah.Should().Be(0m);
    }

    [Fact]
    public async Task A_Refund_That_Reverses_An_Earlier_Period_S_Payment_Is_Marked()
    {
        await ResetAsync();
        await using var setup = Context();
        var userId = await EnsureUserAsync(setup);

        // The customer paid in September and was refunded in October: the two sides of the act are
        // not talking about the same money, so the row says so.
        var order = Order(userId, "INV-1", new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc));
        var capture = Capture(order.Id, "INV-1", new DateTime(2026, 9, 20, 9, 1, 0, DateTimeKind.Utc));
        var refund = Refund(
            order.Id, capture.Id, "INV-1",
            new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
            PaymentStatus.Succeeded);

        setup.Orders.Add(order);
        setup.Payments.AddRange(capture, refund);
        setup.MonobankStatementEntries.Add(Entry("ST-1", "INV-1", 50000));
        await setup.SaveChangesAsync();

        var act = await ActAsync();

        var row = act.RefundsIssued.Should().ContainSingle().Subject;
        row.ReversesEarlierPeriodPayment.Should().BeTrue();
        row.ReversedPaymentAtUtc.Should().Be(new DateTime(2026, 9, 20, 9, 1, 0, DateTimeKind.Utc));
        row.Note.Should().Contain("2026-09-20");
    }

    [Fact]
    public async Task A_Refund_That_Is_Still_In_Flight_Is_Not_Reported()
    {
        await ResetAsync();
        await using var setup = Context();
        var userId = await EnsureUserAsync(setup);

        var order = Order(userId, "INV-1", new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc));
        var capture = Capture(order.Id, "INV-1", new DateTime(2026, 10, 3, 9, 1, 0, DateTimeKind.Utc));
        var refund = Refund(
            order.Id, capture.Id, "INV-1",
            new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
            null,
            PaymentStatus.Pending);

        setup.Orders.Add(order);
        setup.Payments.AddRange(capture, refund);
        await setup.SaveChangesAsync();

        var act = await ActAsync();

        // The money has not left yet: it is a request, not a figure in a signed document. The
        // acquirer's statement is what says whether it arrived.
        act.RefundsIssued.Should().BeEmpty();
        act.Summary.IssuedRefundCount.Should().Be(0);
        act.Summary.IssuedRefundTotalUah.Should().Be(0m);
    }

    [Fact]
    public async Task A_Refund_Issued_In_Another_Period_Belongs_To_That_Period_S_Act()
    {
        await ResetAsync();
        await using var setup = Context();
        var userId = await EnsureUserAsync(setup);

        var order = Order(userId, "INV-1", new DateTime(2026, 9, 3, 9, 0, 0, DateTimeKind.Utc));
        var capture = Capture(order.Id, "INV-1", new DateTime(2026, 9, 3, 9, 1, 0, DateTimeKind.Utc));
        var refund = Refund(
            order.Id, capture.Id, "INV-1",
            new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc),
            PaymentStatus.Succeeded);

        setup.Orders.Add(order);
        setup.Payments.AddRange(capture, refund);
        await setup.SaveChangesAsync();

        var act = await ActAsync();

        act.RefundsIssued.Should().BeEmpty();
        act.Summary.IssuedRefundCount.Should().Be(0);
    }
}
