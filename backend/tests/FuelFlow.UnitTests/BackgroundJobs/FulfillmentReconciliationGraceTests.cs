using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.BackgroundJobs;

/// <summary>
/// The mismatch reconciliation infers "this delivery never happened" from a quantity mismatch alone
/// and then returns the fuel, deleting the fulfillment row that was the only evidence of it. That
/// inference is only safe while the order is young (#217).
/// </summary>
public sealed class FulfillmentReconciliationGraceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly FulfillmentService _service;
    private readonly Mock<ILogger<FulfillmentService>> _loggerMock = new();

    public FulfillmentReconciliationGraceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"grace-{Guid.NewGuid():N}")
            .Options;
        _context = new ApplicationDbContext(options);

        var monobankClientMock = new Mock<IMonobankClient>();
        var refundHandler = new RefundOrderCommandHandler(
            _context,
            new StubMonobankClientFactory(monobankClientMock.Object),
            new ProviderEventService(_context));

        _service = new FulfillmentService(
            _context,
            _loggerMock.Object,
            refundHandler,
            new RuntimeSettingsService(_context),
            NotificationDispatcher.Disabled,
            new ConfigurationBuilder().AddInMemoryCollection().Build());
    }

    public void Dispose() => _context.Dispose();

    /// <summary>One line item claiming one voucher, but three fulfillments recorded - the mismatch.</summary>
    private async Task<Order> SeedMismatchedOrderAsync(TimeSpan age)
    {
        var userId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = OrderKind.Purchase,
            Status = OrderStatus.Fulfilled,
            Price = 100m,
            CreatedAtUtc = DateTime.UtcNow - age,
            FulfilledAtUtc = DateTime.UtcNow - age,
            UpdatedAtUtc = DateTime.UtcNow,
            LineItems =
            [
                new OrderLineItem
                {
                    Id = Guid.NewGuid(),
                    Provider = "OKKO",
                    FuelTypeId = "okko-95",
                    Liters = 10m,
                    Quantity = 1,
                    TermCode = "1m",
                    UnitPrice = 100m,
                    LineTotal = 100m,
                }
            ],
        };
        _context.Orders.Add(order);

        for (var i = 0; i < 3; i++)
        {
            var voucher = new FuelVoucher
            {
                Id = Guid.NewGuid(),
                Provider = "OKKO",
                FuelTypeId = "okko-95",
                Liters = 10m,
                VoucherNumber = $"V-GRACE-{i}",
                QrPayload = Guid.NewGuid().ToString(),
                Status = VoucherStatus.Assigned,
                ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
                CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
                OrderId = order.Id,
                AssignedToUserId = userId,
                CreatedAtUtc = DateTime.UtcNow - age,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            _context.FuelVouchers.Add(voucher);
            _context.Fulfillments.Add(new Fulfillment
            {
                OrderId = order.Id,
                VoucherId = voucher.Id,
                // Newest first, the way the reconciler ranks them when choosing what to remove.
                FulfilledAtUtc = DateTime.UtcNow - age + TimeSpan.FromSeconds(i),
            });
        }

        await _context.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task An_Order_Older_Than_The_Grace_Window_Keeps_Its_Fulfillments_And_Fuel()
    {
        // The dangerous case, and the whole point of the window: a month-old order where a wrong
        // count would strip fuel from a customer who genuinely holds it, and delete the record that
        // they ever had it.
        var order = await SeedMismatchedOrderAsync(age: TimeSpan.FromDays(30));

        await _service.ProcessPendingOrdersAsync();

        (await _context.Fulfillments.IgnoreQueryFilters().CountAsync(f => f.OrderId == order.Id))
            .Should().Be(3, "a month-old mismatch must not be auto-corrected on a count alone");

        (await _context.FuelVouchers.CountAsync(v => v.OrderId == order.Id))
            .Should().Be(3, "the fuel stays with the customer - nothing is returned to stock");
    }

    [Fact]
    public async Task An_Order_Inside_The_Grace_Window_Is_Still_Corrected()
    {
        // The case the window must NOT break: a bad order created a minute ago, where the mismatch is
        // the double-webhook or just-seeded order the reconciler exists to clean up.
        var order = await SeedMismatchedOrderAsync(age: TimeSpan.FromMinutes(1));

        await _service.ProcessPendingOrdersAsync();

        (await _context.Fulfillments.IgnoreQueryFilters().CountAsync(f => f.OrderId == order.Id))
            .Should().Be(1, "inside the window the surplus is still corrected automatically");

        (await _context.FuelVouchers.CountAsync(v => v.OrderId == order.Id))
            .Should().Be(1, "the two surplus vouchers go back to stock");
    }
}