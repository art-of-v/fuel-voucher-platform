using FuelFlow.Features.Brands;
using FuelFlow.Features.Settings;
using FluentAssertions;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.DeleteOrder;
using FuelFlow.Features.Orders.GetAdminOrderById;
using FuelFlow.Features.Orders.GetAdminOrders;
using FuelFlow.Features.Orders.GetAdminPurchases;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Orders.UpdateOrderStatus;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

using FuelFlow.SharedKernel.Observability;
using FuelFlow.Testing;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;
namespace FuelFlow.UnitTests.Orders;

public sealed class OrderAdminCommandHandlersTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IMonobankClient> _monobankClientMock;
    private readonly BulkCheckoutCommandHandler _bulkCheckoutHandler;
    private readonly DeleteOrderCommandHandler _deleteOrderHandler;
    private readonly UpdateOrderStatusCommandHandler _updateOrderStatusHandler;
    private readonly GetAdminOrdersQueryHandler _getAdminOrdersHandler;
    private readonly GetAdminOrderByIdQueryHandler _getAdminOrderByIdHandler;
    private readonly GetAdminPurchasesQueryHandler _getAdminPurchasesHandler;

    public OrderAdminCommandHandlersTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _context.AddMissing();
        SeedFuelTypes();
        SeedFuelPackages();
        SeedDefaultUser(); // active user for checkout tests

        _monobankClientMock = new Mock<IMonobankClient>();
        _monobankClientMock
            .Setup(x => x.CreateInvoiceAsync(It.IsAny<MonobankInvoiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceResponse { InvoiceId = "INV123", PageUrl = "https://pay.test/INV123" });

        var mockMonobankOptions = new Mock<IOptions<MonobankOptions>>();
        mockMonobankOptions.Setup(o => o.Value).Returns(new MonobankOptions
        {
            Token = "test_token",
            WebhookUrl = "https://test.local/webhook",
            RedirectUrl = "https://test.local/redirect",
            BaseUrl = "https://api.test.local",
            Enabled = false
        });

        _bulkCheckoutHandler = new BulkCheckoutCommandHandler(
            _context,
            new StubMonobankClientFactory(_monobankClientMock.Object),
            new StubMonobankMerchantResolver(),
            mockMonobankOptions.Object,
            new RuntimeSettingsService(_context),
            new Mock<ILogger<BulkCheckoutCommandHandler>>().Object,
            NotificationDispatcher.Disabled,
            new FuelFlow.Features.Vouchers.Availability.PackageStockAvailability(_context, new ConfigurationBuilder().Build()));

        _deleteOrderHandler = new DeleteOrderCommandHandler(_context);
        _updateOrderStatusHandler = new UpdateOrderStatusCommandHandler(_context);
        _getAdminOrdersHandler = new GetAdminOrdersQueryHandler(_context);
        _getAdminOrderByIdHandler = new GetAdminOrderByIdQueryHandler(_context);
        _getAdminPurchasesHandler = new GetAdminPurchasesQueryHandler(_context);
    }

    private void SeedFuelTypes()
    {
        var fuelTypes = new[]
        {
            new FuelTypeEntity { Id = ToUuid("okko-dp"), Name = "ДП ЄВРО", BrandId = BrandIds.Okko, CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow },
            new FuelTypeEntity { Id = ToUuid("okko-95"), Name = "A-95", BrandId = BrandIds.Okko, CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow },
            new FuelTypeEntity { Id = ToUuid("okko-p95"), Name = "Pulls 95", BrandId = BrandIds.Okko, CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow },
            new FuelTypeEntity { Id = ToUuid("wog-dp"), Name = "ДП Mustang", BrandId = BrandIds.Wog, CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow },
            new FuelTypeEntity { Id = ToUuid("wog-95"), Name = "A-95 Mustang", BrandId = BrandIds.Wog, CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow }
        };
        _context.FuelTypes.AddRange(fuelTypes);
        _context.SaveChanges();
    }

    private void SeedFuelPackages()
    {
        // Seeded as offers plus volumes: the order handlers price and resolve against the offer.
        _context.SeedPackage("pkg-okko-95-50", BrandIds.Okko, "okko-95", 50m, 2500m,
            supplierPricePerLiter: 49m, marginUahPerLiter: 1m, finalPricePerLiter: 50m);
        _context.SeedPackage("pkg-okko-p95-50", BrandIds.Okko, "okko-p95", 50m, 2900m,
            supplierPricePerLiter: 57m, marginUahPerLiter: 1m, finalPricePerLiter: 58m);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    /// <summary>
    /// Checkout refuses a package with nothing sellable behind it, so a test that expects a completed
    /// sale has to put an activated voucher under every line it buys.
    /// </summary>
    private void SeedSellableStock(string fuelTypeId, decimal liters = 50m)
    {
        var expiry = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1));
        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid(fuelTypeId),
            Liters = liters,
            SupplierExpirationDate = expiry,
            CustomerExpirationDate = expiry,
            VoucherNumber = $"STOCK-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = VoucherStatus.Available,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };
        _context.FuelVouchers.Add(voucher);
        // The stock queries read the summary, not the vouchers, and the in-memory provider has no
        // trigger to keep it in step. The row is derived from the voucher, so it cannot disagree
        // with it - see StockSummaryFollowsTheVouchers for the real thing.
        _context.StockSummary.Add(TestStock.SummaryFor(voucher));
        _context.SaveChanges();
    }

    private static Order BuildOrder(Guid userId, OrderStatus status = OrderStatus.PendingPayment) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Price = 2500,
        Status = status,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow,
        LineItems =
        {
            new OrderLineItem
            {
                Id = Guid.NewGuid(),
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = ToUuid("okko-95"),
                Liters = 50,
                Quantity = 1,
                UnitPrice = 2500,
                LineTotal = 2500
            }
        }
    };

    /// <summary>
    /// The money record of a paid order: a capture in state Succeeded, which is the only state
    /// that means the money is ours.
    /// </summary>
    private void SeedCapturedPayment(Guid orderId)
        => SeedPayment(orderId, FuelFlow.Payments.SharedModels.PaymentStatus.Succeeded);

    private void SeedPayment(Guid orderId, FuelFlow.Payments.SharedModels.PaymentStatus status)
    {
        _context.Payments.Add(new FuelFlow.Payments.SharedModels.Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Kind = FuelFlow.Payments.SharedModels.PaymentKind.Capture,
            Provider = "monobank",
            ProviderInvoiceId = $"INV-{orderId:N}",
            AmountUah = 2500,
            AmountKopecks = 250000,
            Currency = "UAH",
            Status = status,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        _context.SaveChanges();
    }

    // `brandId` rather than a default of `BrandIds.Okko`: a static readonly Guid is not a
    // compile-time constant, so it cannot be a default parameter value. Callers that omit it get
    // the same brand the helper always meant.
    private static CheckoutItem CheckoutItem(string fuelTypeId, int price, Guid? brandId = null) => new()
    {
        
        FuelTypeId = ToUuid(fuelTypeId),
        Liters = 50,
        Quantity = 1,
        Price = price,
        BrandId = brandId ?? BrandIds.Okko,
        BrandName = "okko"
    };

    [Fact]
    public async Task DeleteOrder_SoftDeletesExistingOrder_AndReturnsTrue()
    {
        var order = BuildOrder(Guid.NewGuid());
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _deleteOrderHandler.HandleAsync(new DeleteOrderCommand(order.Id));

        result.Should().BeTrue();

        // #217: soft delete, so the row survives as history. Both halves matter - asserting only
        // that reads are clean would pass with a hard delete too, and asserting only that the row
        // survives would pass with a change that never persisted.
        var stored = _context.Orders.IgnoreQueryFilters().SingleOrDefault(o => o.Id == order.Id);
        stored.Should().NotBeNull("an order is history, not garbage - the row must survive");
        stored!.IsDeleted.Should().BeTrue();
        (_context.Orders.Count(o => o.Id == order.Id)).Should().Be(0,
            "the !IsDeleted query filter must hide it from every normal read");
    }

    [Fact]
    public async Task DeleteOrder_ShouldReturnFalse_WhenOrderMissing()
    {
        var result = await _deleteOrderHandler.HandleAsync(new DeleteOrderCommand(Guid.NewGuid()));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateOrderStatus_MovesAPaidOrderToFulfilment_WhenTheMoneyIsThere()
    {
        var order = BuildOrder(Guid.NewGuid(), OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        SeedCapturedPayment(order.Id);

        var result = await _updateOrderStatusHandler.HandleAsync(new UpdateOrderStatusCommand(order.Id, "PendingFulfillment"));

        result.Success.Should().BeTrue();
        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingFulfillment);
        updated.UpdatedAtUtc.Should().BeCloseTo(TestClock.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task UpdateOrderStatus_RefusesToFulfil_AnOrderThatWasNeverPaidFor()
    {
        // The defect this exists for: the endpoint wrote any status, and the fulfilment job
        // selects its work by status alone - so an unpaid order could be handed fuel.
        var order = BuildOrder(Guid.NewGuid(), OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _updateOrderStatusHandler.HandleAsync(new UpdateOrderStatusCommand(order.Id, "PendingFulfillment"));

        result.Found.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("payment");
        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingPayment);
    }

    [Fact]
    public async Task UpdateOrderStatus_RefusesToFulfil_AnOrderWhosePaymentIsOnlyPending()
    {
        // An invoice exists and nobody paid it: the payment row says Pending, so the money has
        // not arrived either.
        var order = BuildOrder(Guid.NewGuid(), OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        SeedPayment(order.Id, FuelFlow.Payments.SharedModels.PaymentStatus.Pending);

        var result = await _updateOrderStatusHandler.HandleAsync(new UpdateOrderStatusCommand(order.Id, "PendingFulfillment"));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("payment");
    }

    [Fact]
    public async Task UpdateOrderStatus_AbandonsAnUnpaidOrder()
    {
        var order = BuildOrder(Guid.NewGuid(), OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _updateOrderStatusHandler.HandleAsync(new UpdateOrderStatusCommand(order.Id, "Cancelled"));

        result.Success.Should().BeTrue();
        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task UpdateOrderStatus_RefusesToCancel_APaidOrder()
    {
        // Cancelling a paid order would keep the money and the goods. That is the refund path's
        // business, and nobody else's.
        var order = BuildOrder(Guid.NewGuid(), OrderStatus.Paid);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _updateOrderStatusHandler.HandleAsync(new UpdateOrderStatusCommand(order.Id, "Cancelled"));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("refunded");
        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public async Task UpdateOrderStatus_RefusesATransitionTheOperatorMayNotMake()
    {
        var order = BuildOrder(Guid.NewGuid(), OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _updateOrderStatusHandler.HandleAsync(new UpdateOrderStatusCommand(order.Id, "Fulfilled"));

        result.Success.Should().BeFalse();
        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingPayment);
    }

    [Fact]
    public async Task UpdateOrderStatus_LeavesStatusUnchanged_WhenStatusInvalid()
    {
        var order = BuildOrder(Guid.NewGuid(), OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _updateOrderStatusHandler.HandleAsync(new UpdateOrderStatusCommand(order.Id, "NotARealStatus"));

        result.Found.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("NotARealStatus");
        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingPayment);
    }

    [Fact]
    public async Task UpdateOrderStatus_ReturnsNotFound_WhenOrderMissing()
    {
        var result = await _updateOrderStatusHandler.HandleAsync(new UpdateOrderStatusCommand(Guid.NewGuid(), "Fulfilled"));

        result.Found.Should().BeFalse();
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task BulkCheckout_ShouldCreateSingleOrder_WithAllLineItems_AndSharedInvoice()
    {
        var user = await _context.Users.FirstAsync();
        SeedSellableStock("okko-95");
        SeedSellableStock("okko-p95");
        var command = new BulkCheckoutCommand
        {
            UserId = user.Id,
            Items = new List<CheckoutItem>
            {
                CheckoutItem("okko-95", 2500),
                CheckoutItem("okko-p95", 2900)
            }
        };

        var response = await _bulkCheckoutHandler.HandleAsync(command);

        response.OrderIds.Should().ContainSingle();
        response.MonobankInvoiceId.Should().Be("INV123");
        response.PaymentUrl.Should().Be("https://pay.test/INV123");

        _monobankClientMock.Verify(
            x => x.CreateInvoiceAsync(It.IsAny<MonobankInvoiceRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);

        var order = await _context.Orders
            .Include(o => o.LineItems)
            .SingleOrDefaultAsync(o => o.Id == response.OrderIds[0]);

        order.Should().NotBeNull();
        order!.UserId.Should().Be(user.Id);
        order.Price.Should().Be(5400);
        order.Status.Should().Be(OrderStatus.PendingPayment);
        order.MonobankInvoiceId.Should().Be("INV123");
        order.MonobankPaymentUrl.Should().Be("https://pay.test/INV123");
        order.LineItems.Should().HaveCount(2);
        order.LineItems.Should().Contain(li => li.FuelTypeId == ToUuid("okko-95"));
        order.LineItems.Should().Contain(li => li.FuelTypeId == ToUuid("okko-p95"));
    }

    [Fact]
    public async Task BulkCheckout_ShouldThrow_WhenFuelTypeInvalid()
    {
        var user = await _context.Users.FirstAsync();
        var command = new BulkCheckoutCommand
        {
            UserId = user.Id,
            Items = new List<CheckoutItem> { CheckoutItem("unknown-fuel", 2500) }
        };

        Func<Task> act = () => _bulkCheckoutHandler.HandleAsync(command);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid fuel type*");

        _context.Orders.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAdminOrders_ShouldReturnOrders_IncludingSoftDeleted()
    {
        var active = BuildOrder(Guid.NewGuid(), OrderStatus.Fulfilled);
        active.FulfilledAtUtc = TestClock.UtcNow;
        var softDeleted = BuildOrder(Guid.NewGuid(), OrderStatus.Cancelled);
        softDeleted.IsDeleted = true;

        _context.Orders.AddRange(active, softDeleted);
        await _context.SaveChangesAsync();

        var result = await _getAdminOrdersHandler.HandleAsync(new GetAdminOrdersQuery());

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.Should().Contain(dto => dto.Id == active.Id && dto.Status == OrderStatus.Fulfilled.ToString());
        result.Items.Should().Contain(dto => dto.Id == softDeleted.Id && dto.Status == OrderStatus.Cancelled.ToString());
        result.Items.Should().Contain(dto => dto.Id == active.Id && dto.LineItems.Count == 1);
    }

    [Fact]
    public async Task GetAdminOrderById_ShouldReturnOrder_WhenFound()
    {
        var order = BuildOrder(Guid.NewGuid(), OrderStatus.Paid);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _getAdminOrderByIdHandler.HandleAsync(new GetAdminOrderByIdQuery(order.Id));

        result.Should().NotBeNull();
        result!.Id.Should().Be(order.Id);
        result.UserId.Should().Be(order.UserId);
        result.Status.Should().Be(OrderStatus.Paid.ToString());
        result.LineItems.Should().ContainSingle();
    }

    [Fact]
    public async Task GetAdminOrderById_ShouldReturnNull_WhenMissing()
    {
        var result = await _getAdminOrderByIdHandler.HandleAsync(new GetAdminOrderByIdQuery(Guid.NewGuid()));

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAdminPurchases_ShouldReturnOrders_WithVoucherCount()
    {
        var order = BuildOrder(Guid.NewGuid(), OrderStatus.Fulfilled);
        order.FulfilledAtUtc = TestClock.UtcNow;
        order.Fulfillments.Add(new Fulfillment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = TestClock.UtcNow,
            Voucher = new FuelVoucher
            {
                Id = Guid.NewGuid(),
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = ToUuid("okko-95"),
                Liters = 50,
                VoucherNumber = "V-test",
                QrPayload = "q",
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddDays(30)),
                CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddDays(30)),
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            }
        });

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _getAdminPurchasesHandler.HandleAsync(new GetAdminPurchasesQuery());

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be(order.Id);
        result.Items[0].VoucherCount.Should().Be(1);
    }

    private void SeedDefaultUser()
    {
        var role = new Role { Id = SeedRoles.UserRoleId, Name = SeedRoles.UserName, CreatedAtUtc = TestClock.UtcNow };
        _context.Roles.Add(role);
        _context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380991234567",
            RoleId = role.Id,
            Role = role,
            IsActive = true,
            IsDeleted = false,
            TokenVersion = 1,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        _context.SaveChanges();
    }
}

