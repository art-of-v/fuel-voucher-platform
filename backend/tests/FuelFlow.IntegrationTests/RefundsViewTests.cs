using FluentAssertions;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The <c>refunds</c> view sees every refund, including one that has no order.
/// </summary>
/// <remarks>
/// The view joined <c>payments</c> to <c>orders</c> with an INNER join, while the shape
/// constraint (<c>ck_payments_refund_shape</c>) requires a refund to have a
/// <c>parent_payment_id</c> and says nothing about an <c>order_id</c>. A refund without an order
/// was therefore invisible in the view, with no error anywhere - the row was in the table, and
/// the human's view of refunds silently omitted it.
///
/// The view exists for humans (a spreadsheet, a psql session), so "the row is in the table" is
/// not the same thing as "the refund is visible".
/// </remarks>
[Collection("Integration Tests")]
public sealed class RefundsViewTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public RefundsViewTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task A_Refund_Without_An_Order_Is_In_The_View()
    {
        var captureId = Guid.NewGuid();
        var refundId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            context.Payments.Add(Capture(captureId));
            context.Payments.Add(Refund(refundId, captureId, orderId: null));
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();

        var inView = await verify.Database
            .SqlQueryRaw<Guid>("SELECT id FROM refunds WHERE id = {0}", refundId)
            .ToListAsync();

        inView.Should().ContainSingle(
            "the row is in the table, but a view that omits it is a defect waiting for its first occurrence");
    }

    [Fact]
    public async Task A_Refund_With_An_Order_Is_Still_In_The_View()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var refundId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            context.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = $"+38{userId:N}"[..20],
                IsActive = true,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });

            context.Orders.Add(new Order
            {
                Id = orderId,
                UserId = userId,
                Price = 100m,
                Status = OrderStatus.Refunded,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });

            context.Payments.Add(Capture(captureId, orderId));
            context.Payments.Add(Refund(refundId, captureId, orderId));
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();

        var rows = await verify.Database
            .SqlQueryRaw<string>(
                """
                SELECT COALESCE(user_id::text, 'none') AS "Value"
                FROM refunds
                WHERE id = {0}
                """, refundId)
            .ToListAsync();

        rows.Should().ContainSingle().Which.Should().Be(userId.ToString(),
            "the order's user is still projected through the join");
    }

    private static Payment Capture(Guid id, Guid? orderId = null) => new()
    {
        Id = id,
        OrderId = orderId,
        Kind = PaymentKind.Capture,
        Provider = "monobank",
        AmountUah = 100m,
        AmountKopecks = 10_000m,
        Currency = "UAH",
        Status = PaymentStatus.Succeeded,
        PaidAtUtc = TestClock.UtcNow,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    private static Payment Refund(Guid id, Guid parentPaymentId, Guid? orderId) => new()
    {
        Id = id,
        OrderId = orderId,
        Kind = PaymentKind.Refund,
        ParentPaymentId = parentPaymentId,
        Provider = "monobank",
        AmountUah = 100m,
        AmountKopecks = 10_000m,
        Currency = "UAH",
        Status = PaymentStatus.Succeeded,
        PaidAtUtc = TestClock.UtcNow,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };
}
