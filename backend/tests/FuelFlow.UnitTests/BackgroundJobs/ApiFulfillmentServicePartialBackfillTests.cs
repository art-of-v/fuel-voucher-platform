using FluentAssertions;
using FuelFlow.API.BackgroundJobs.Models;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace FuelFlow.UnitTests.BackgroundJobs;

/// <summary>
/// Covers the partial-fulfilment backfill: an order left <c>PartiallyFulfilled</c> because stock ran
/// out must be completed by a later run once a matching voucher becomes available, and must then
/// flip to <c>Fulfilled</c>.
/// </summary>
/// <remarks>
/// Ported from the standalone worker's own copy of the service, which was deleted once it became
/// clear the API holds the only implementation (see the alias block in
/// <c>backend/src/FuelFlow.JobsWorker/Program.cs</c>). The scenario was worth keeping - it is the
/// safety net that keeps a customer from paying for fuel they never receive.
/// </remarks>
public sealed class ApiFulfillmentServicePartialBackfillTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly TestableFulfillmentService _service;

    public ApiFulfillmentServicePartialBackfillTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);

        var monobankClientMock = new Mock<IMonobankClient>();
        monobankClientMock
            .Setup(x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankCancelResponse { Status = "processing" });

        _service = new TestableFulfillmentService(
            _context,
            new Mock<ILogger<FuelFlow.API.BackgroundJobs.FulfillmentService>>().Object,
            new RefundOrderCommandHandler(_context, monobankClientMock.Object, new ProviderEventService(_context)),
            new RuntimeSettingsService(_context),
            new ConfigurationBuilder().AddInMemoryCollection().Build());
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    /// <summary>
    /// In-memory EF cannot run the service's raw-SQL claim/flip statements, so both are replaced
    /// with equivalent tracked equivalents. Production behaviour is covered by
    /// <c>FulfillmentConcurrencyIntegrationTests</c> against a real Postgres.
    /// </summary>
    private sealed class TestableFulfillmentService : FuelFlow.API.BackgroundJobs.FulfillmentService
    {
        private readonly ApplicationDbContext _db;

        public TestableFulfillmentService(
            ApplicationDbContext context,
            ILogger<FuelFlow.API.BackgroundJobs.FulfillmentService> logger,
            RefundOrderCommandHandler refundHandler,
            RuntimeSettingsService settings,
            IConfiguration configuration)
            : base(context, logger, refundHandler, settings, FuelFlow.SharedKernel.Observability.NotificationDispatcher.Disabled, configuration)
        {
            _db = context;
        }

        protected internal override async Task<int> TryMarkOrderFulfilledAsync(Guid orderId, CancellationToken cancellationToken)
        {
            var order = await _db.Orders.FindAsync([orderId], cancellationToken);
            if (order != null && (order.Status == OrderStatus.PendingFulfillment || order.Status == OrderStatus.PartiallyFulfilled))
            {
                order.Status = OrderStatus.Fulfilled;
                order.FulfilledAtUtc = DateTime.UtcNow;
                order.UpdatedAtUtc = DateTime.UtcNow;
                return await _db.SaveChangesAsync(cancellationToken);
            }
            return 0;
        }

        protected internal override async Task<int> TryAssignVoucherAsync(Guid voucherId, Guid userId, Guid? legalEntityId, CancellationToken cancellationToken)
        {
            var voucher = await _db.FuelVouchers.FindAsync([voucherId], cancellationToken);
            if (voucher != null && voucher.Status == VoucherStatus.Available)
            {
                voucher.Status = VoucherStatus.Assigned;
                voucher.AssignedToUserId = userId;
                voucher.LegalEntityId = legalEntityId;
                voucher.WorkerUserId = null;
                voucher.UpdatedAtUtc = DateTime.UtcNow;
                return await _db.SaveChangesAsync(cancellationToken);
            }
            return 0;
        }
    }

    [Fact]
    public async Task ProcessPendingOrders_ShouldBackfillPartiallyFulfilledOrder_WhenNewVoucherBecomesAvailable()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 2000,
            Status = OrderStatus.PartiallyFulfilled,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            LineItems = new List<OrderLineItem>
            {
                new OrderLineItem
                {
                    Provider = "OKKO",
                    FuelTypeId = "okko-95",
                    Liters = 20,
                    Quantity = 2,
                    UnitPrice = 1000,
                    LineTotal = 2000
                }
            }
        };

        var existingAssignedVoucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 20,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            VoucherNumber = "OKKO-ASSIGNED-1",
            QrPayload = "assigned-1",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            UpdatedAtUtc = DateTime.UtcNow
        };

        var existingFulfillment = new Fulfillment
        {
            OrderId = orderId,
            VoucherId = existingAssignedVoucher.Id,
            FulfilledAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };

        var newAvailableVoucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 20,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2),
            VoucherNumber = "OKKO-AVAILABLE-2",
            QrPayload = "available-2",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        _context.FuelVouchers.AddRange(existingAssignedVoucher, newAvailableVoucher);
        _context.Fulfillments.Add(existingFulfillment);
        await _context.SaveChangesAsync();

        await _service.ProcessPendingOrdersAsync();

        var updatedOrder = await _context.Orders.FindAsync(orderId);
        updatedOrder.Should().NotBeNull();
        updatedOrder!.Status.Should().Be(OrderStatus.Fulfilled);
        updatedOrder.FulfilledAtUtc.Should().NotBeNull();

        var totalFulfillments = await _context.Fulfillments.CountAsync(f => f.OrderId == orderId);
        totalFulfillments.Should().Be(2);

        var updatedNewVoucher = await _context.FuelVouchers.FindAsync(newAvailableVoucher.Id);
        updatedNewVoucher.Should().NotBeNull();
        updatedNewVoucher!.Status.Should().Be(VoucherStatus.Assigned);
        updatedNewVoucher.AssignedToUserId.Should().Be(userId);
    }
}