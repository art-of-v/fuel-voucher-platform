using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The order status change and the <c>order_events</c> row that records it are one unit of work.
/// </summary>
/// <remarks>
/// EF opens a separate implicit transaction per <c>SaveChanges</c>. The guarded save in
/// <c>ApplicationDbContext</c> makes two of them - one for the change, one for the event row that
/// records it - so without an explicit transaction around them the change could land and the
/// event could fail: an order that moved with no record of why, and no way to tell afterwards
/// whether the event was lost or never written.
///
/// The test makes the SECOND save fail on purpose and then asks the database what survived.
/// </remarks>
[Collection("Integration Tests")]
public sealed class OrderEventAtomicityTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public OrderEventAtomicityTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString);

        if (interceptor is not null)
            builder.AddInterceptors(interceptor);

        return new ApplicationDbContext(builder.Options, TestQrCipher.Instance);
    }

    [Fact]
    public async Task The_Status_Change_Does_Not_Survive_A_Failed_Event_Row()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            seed.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = $"+38{Guid.NewGuid():N}"[..20],
                IsActive = true,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });

            AddPaidOrder(seed, new Order
            {
                Id = orderId,
                UserId = userId,
                Price = 100m,
                Status = OrderStatus.PendingFulfillment,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });

            await seed.SaveChangesAsync();
        }

        await using (var write = CreateContext(new FailOnTheSecondSave()))
        {
            var order = await write.Orders.SingleAsync(o => o.Id == orderId);
            order.Status = OrderStatus.Fulfilled;
            order.FulfilledAtUtc = TestClock.UtcNow;

            var act = async () => await write.SaveChangesAsync();

            await act.Should().ThrowAsync<InvalidOperationException>(
                "the second save is the one that writes the event row");
        }

        await using var verify = CreateContext();

        var stored = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);

        stored.Status.Should().Be(OrderStatus.PendingFulfillment,
            "the status change and the event row are one unit of work: if the event cannot be " +
            "written, the change must not stand");

        // The seed wrote the order, so it wrote a "Created" event with it. What must NOT be there
        // is the "StatusChanged" event - the one the failed second save would have written.
        var statusChanges = await verify.OrderEvents.AsNoTracking()
            .CountAsync(e => e.OrderId == orderId && e.EventType == "StatusChanged");

        statusChanges.Should().Be(0, "the event row never landed, and neither did the change it records");
    }

    /// <summary>
    /// Fails the second <c>SaveChanges</c> of the unit of work - the one that writes the
    /// <c>order_events</c> row.
    /// </summary>
    private sealed class FailOnTheSecondSave : SaveChangesInterceptor
    {
        private int _saves;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            _saves++;

            if (_saves > 1)
                throw new InvalidOperationException("the second save is the one that writes the event row");

            return ValueTask.FromResult(result);
        }
    }
    /// <summary>
    /// Adds an order AND the money record of a paid order. Fulfilment asks the payment row
    /// whether the money arrived before it hands out a voucher, so a fixture that expects
    /// vouchers has to say the order was paid for.
    /// </summary>
    private static void AddPaidOrder(ApplicationDbContext seed, Order order)
    {
        seed.Orders.Add(order);
        seed.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Kind = PaymentKind.Capture,
            Provider = "monobank",
            ProviderInvoiceId = $"INV-{order.Id:N}",
            AmountUah = order.Price,
            AmountKopecks = (long)(order.Price * 100),
            Currency = "UAH",
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = TestClock.UtcNow,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
    }
}
