using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Features.Orders.SimulatePayment;
using FuelFlow.Features.Orders.UpdateMonobankInfo;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.UnitTests.Orders;

public class OrderCommandHandlersTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly CreateCheckoutCommandHandler _createCheckoutHandler;
    private readonly BulkCheckoutCommandHandler _bulkCheckoutHandler;
    private readonly GetUserPurchasesCommandHandler _getUserPurchasesHandler;
    private readonly SimulatePaymentCommandHandler _simulatePaymentHandler;
    private readonly UpdateMonobankInfoCommandHandler _updateMonobankInfoHandler;
    private readonly Mock<IMonobankClient> _monobankClientMock;

public OrderCommandHandlersTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new ApplicationDbContext(options);
            SeedFuelTypes();
            SeedFuelPackages();
            SeedDefaultUser(); // active user for checkout tests

            var createCheckoutLogger = new Mock<ILogger<CreateCheckoutCommandHandler>>().Object;
        var bulkCheckoutLogger = new Mock<ILogger<BulkCheckoutCommandHandler>>().Object;
        var getUserPurchasesLogger = new Mock<ILogger<GetUserPurchasesCommandHandler>>().Object;
        var simulatePaymentLogger = new Mock<ILogger<SimulatePaymentCommandHandler>>().Object;
        var updateMonobankInfoLogger = new Mock<ILogger<UpdateMonobankInfoCommandHandler>>().Object;

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

        var qrGeneratorMock = new Mock<IQrGenerator>();
        qrGeneratorMock.Setup(x => x.GenerateQrCode(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Returns("qr-code-data");

        _createCheckoutHandler = new CreateCheckoutCommandHandler(_context, _monobankClientMock.Object, mockMonobankOptions.Object, createCheckoutLogger, new FuelFlow.SharedKernel.Observability.FuelFlowMetrics());
        _bulkCheckoutHandler = new BulkCheckoutCommandHandler(_context, _monobankClientMock.Object, mockMonobankOptions.Object, bulkCheckoutLogger);
        _getUserPurchasesHandler = new GetUserPurchasesCommandHandler(_context, qrGeneratorMock.Object, getUserPurchasesLogger);
        _simulatePaymentHandler = new SimulatePaymentCommandHandler(_context, _getUserPurchasesHandler, simulatePaymentLogger, new Mock<IBackgroundJobClient>().Object);
        _updateMonobankInfoHandler = new UpdateMonobankInfoCommandHandler(_context, updateMonobankInfoLogger);
    }

    private void SeedFuelTypes()
    {
        var fuelTypes = new[]
        {
            new FuelTypeEntity { Id = "okko-dp", Name = "ДП ЄВРО", StationId = "okko", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "okko-95", Name = "A-95", StationId = "okko", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "okko-p95", Name = "Pulls 95", StationId = "okko", BasePrice = 62, DiscountPrice = 58, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "wog-dp", Name = "ДП Mustang", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "wog-95", Name = "A-95 Mustang", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow }
        };
        _context.FuelTypes.AddRange(fuelTypes);
        _context.SaveChanges();
    }

    private void SeedFuelPackages()
    {
        var packages = new[]
        {
            new FuelPackage
            {
                Id = "pkg-okko-95-50",
                StationId = "okko",
                FuelTypeId = "okko-95",
                FuelName = "A-95",
                Liters = 50m,
                Price = 2500,
                OriginalPrice = 2450,
                SupplierPricePerLiter = 49m,
                MarginUahPerLiter = 1m,
                FinalPricePerLiter = 50m,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            }
        };
        _context.FuelPackages.AddRange(packages);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private CreateCheckoutCommand CheckoutCommand() => new()
    {
        UserId = _context.Users.First().Id,
        Provider = "okko",
        FuelTypeId = "okko-95",
        StationId = "okko",
        StationName = "OKKO",
        Liters = 50,
        Quantity = 1,
        Price = 2500
    };

    private BulkCheckoutCommand BulkCommand() => new()
    {
        UserId = _context.Users.First().Id,
        Items =
        {
            new CheckoutItem
            {
                Provider = "okko",
                FuelTypeId = "okko-95",
                StationId = "okko",
                StationName = "OKKO",
                Liters = 50,
                Quantity = 1,
                Price = 2500
            }
        }
    };

    /// <summary>
    /// Seeds a supplier+fuel whose recomputed price/л sits below supplier cost: cost 50,
    /// margin 2 (cost-plus 52) but a pump ceiling of 49 − 0.5 = 48.5 binds → final 48.5 &lt; 50.
    /// <paramref name="allowBelowCost"/> flips the manager opt-in on the fuel type.
    /// </summary>
    private void SeedBelowCostFuel(bool allowBelowCost)
    {
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "okko-loss", Name = "ДП збиток", StationId = "okko",
            BasePrice = 49, DiscountPrice = 49, AllowBelowCost = allowBelowCost,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelPackages.Add(new FuelPackage
        {
            Id = "pkg-okko-loss-50", StationId = "okko", FuelTypeId = "okko-loss",
            FuelName = "ДП збиток", Liters = 50m, Price = 2425, OriginalPrice = 2450,
            SupplierPricePerLiter = 50m, MarginUahPerLiter = 2m,
            PumpPricePerLiter = 49m, MinDiscountPerLiter = 0.5m, FinalPricePerLiter = 48.5m,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    private CreateCheckoutCommand BelowCostCommand() => new()
    {
        UserId = _context.Users.First().Id,
        Provider = "okko", FuelTypeId = "okko-loss", StationId = "okko", StationName = "OKKO",
        Liters = 50, Quantity = 1, Price = 2425
    };

    private BulkCheckoutCommand BelowCostBulkCommand() => new()
    {
        UserId = _context.Users.First().Id,
        Items =
        {
            new CheckoutItem
            {
                Provider = "okko", FuelTypeId = "okko-loss", StationId = "okko", StationName = "OKKO",
                Liters = 50, Quantity = 1, Price = 2425
            }
        }
    };

    private Order BuildOrder(OrderStatus status, string? monobankPaymentUrl = null) => new()    {
        Id = Guid.NewGuid(),
        UserId = _context.Users.First().Id,
        Price = 2500,
        Status = status,
        MonobankPaymentUrl = monobankPaymentUrl,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        LineItems =
        {
            new OrderLineItem
            {
                Id = Guid.NewGuid(),
                Provider = "okko",
                FuelTypeId = "okko-95",
                Liters = 50,
                Quantity = 1,
                UnitPrice = 2500,
                LineTotal = 2500
            }
        }
    };

    [Fact]
public async Task CreateCheckout_ShouldCreateOrder_WithCorrectDetails()
    {
        var response = await _createCheckoutHandler.HandleAsync(CheckoutCommand());

        Assert.NotEqual(Guid.Empty, response.OrderId);
        Assert.Equal(OrderStatus.PendingPayment.ToString(), response.Status);
        Assert.Equal("INV123", response.MonobankInvoiceId);

        var order = await _context.Orders.FindAsync(response.OrderId);
        Assert.NotNull(order);
        Assert.Equal(2500, order.Price);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);

        var lineItem = Assert.Single(order.LineItems);
        Assert.Equal("okko", lineItem.Provider);
        Assert.Equal("okko-95", lineItem.FuelTypeId);
        Assert.Equal(50, lineItem.Liters);
        Assert.Equal(1, lineItem.Quantity);
        Assert.Equal(2500, lineItem.LineTotal);
    }

    [Fact]
    public async Task CreateCheckout_ShouldChargeFractionalKopecks_AndMatchMonobankAmountExactly()
    {
        // #90 decimal money: a fractional per-litre price must survive to the kopeck all the way to
        // the Monobank invoice. 43.00 cost + 2.67 margin = 45.67/л (no pump ceiling) × 10 л = 456.70 UAH.
        // Before #90 this collapsed to a whole 457 UAH and would then mismatch the success webhook.
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "okko-frac", Name = "ДП дробові", StationId = "okko",
            BasePrice = 50, DiscountPrice = 48,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelPackages.Add(new FuelPackage
        {
            Id = "pkg-okko-frac-10", StationId = "okko", FuelTypeId = "okko-frac",
            FuelName = "ДП дробові", Liters = 10m,
            Price = 4567, OriginalPrice = 4567,                     // stale frozen values, ignored by ServerPricing
            SupplierPricePerLiter = 43m, MarginUahPerLiter = 2.67m, // cost-plus 45.67/л, ≥ cost → not below-cost
            FinalPricePerLiter = 45.67m,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        MonobankInvoiceRequest? captured = null;
        _monobankClientMock
            .Setup(x => x.CreateInvoiceAsync(It.IsAny<MonobankInvoiceRequest>(), It.IsAny<CancellationToken>()))
            .Callback<MonobankInvoiceRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new MonobankInvoiceResponse { InvoiceId = "INV123", PageUrl = "https://pay.test/INV123" });

        var command = new CreateCheckoutCommand
        {
            UserId = _context.Users.First().Id,
            Provider = "okko", FuelTypeId = "okko-frac", StationId = "okko", StationName = "OKKO",
            Liters = 10, Quantity = 1, Price = 4567   // client price is ignored; server recomputes 456.70
        };

        var response = await _createCheckoutHandler.HandleAsync(command);

        var order = await _context.Orders.FindAsync(response.OrderId);
        Assert.NotNull(order);
        Assert.Equal(456.70m, order!.Price);          // kopecks preserved, not rounded up to 457

        var lineItem = Assert.Single(order.LineItems);
        Assert.Equal(456.70m, lineItem.UnitPrice);
        Assert.Equal(456.70m, lineItem.LineTotal);

        // Order.Price = Σ line totals (exact 2 dp) → exact integer kopecks → success-webhook amount match.
        Assert.NotNull(captured);
        Assert.Equal(45670L, captured!.Amount);
    }

    [Fact]
    public async Task CreateCheckout_ShouldReuseExistingOrder_WhenDuplicateIdempotencyKeyWithinHour()
    {
        var user = await _context.Users.FirstAsync();
        var response1 = await _createCheckoutHandler.HandleAsync(CheckoutCommand());

        var response2 = await _createCheckoutHandler.HandleAsync(CheckoutCommand());

        Assert.Equal(response1.OrderId, response2.OrderId);
        Assert.Equal(response1.MonobankInvoiceId, response2.MonobankInvoiceId);

        var orders = await _context.Orders.Where(o => o.UserId == user.Id).ToListAsync();
        Assert.Single(orders);
    }

    [Fact]
    public async Task CreateCheckout_ShouldCreateNewOrder_WhenPreviousOrderInBucketIsAlreadyPaid()
    {
        var user = await _context.Users.FirstAsync();
        var response1 = await _createCheckoutHandler.HandleAsync(CheckoutCommand());

        // User pays immediately; the bucket window must not block a
        // legitimate repeat purchase of the same fuel/quantity.
        var paidOrder = await _context.Orders.FindAsync(response1.OrderId);
        paidOrder!.Status = OrderStatus.PendingFulfillment;
        await _context.SaveChangesAsync();

        var response2 = await _createCheckoutHandler.HandleAsync(CheckoutCommand());

        Assert.NotEqual(response1.OrderId, response2.OrderId);

        var orders = await _context.Orders.Where(o => o.UserId == user.Id).ToListAsync();
        Assert.Equal(2, orders.Count);
        Assert.Equal(2, orders.Select(o => o.IdempotencyKey).Distinct().Count());
    }

    [Fact]
    public async Task BulkCheckout_ShouldPersistIdempotencyKey()
    {
        // Regression guard for #16: the bulk handler used to leave IdempotencyKey blank, so the
        // unique index / dedup never engaged and retries double-charged.
        var response = await _bulkCheckoutHandler.HandleAsync(BulkCommand());

        var order = await _context.Orders.FindAsync(response.OrderIds.Single());
        Assert.NotNull(order);
        Assert.False(string.IsNullOrEmpty(order!.IdempotencyKey));
    }

    [Fact]
    public async Task BulkCheckout_ShouldReuseExistingOrder_WhenDuplicateWithinBucket()
    {
        var user = await _context.Users.FirstAsync();
        var response1 = await _bulkCheckoutHandler.HandleAsync(BulkCommand());

        // Mirrors the mobile fetchWithRetry auto-resend: an identical /bulk POST while the first
        // order is still awaiting payment must collapse onto that order + invoice (#16).
        var response2 = await _bulkCheckoutHandler.HandleAsync(BulkCommand());

        Assert.Equal(response1.OrderIds.Single(), response2.OrderIds.Single());
        Assert.Equal(response1.MonobankInvoiceId, response2.MonobankInvoiceId);

        var orders = await _context.Orders.Where(o => o.UserId == user.Id).ToListAsync();
        Assert.Single(orders);
    }

    [Fact]
    public async Task BulkCheckout_ShouldCreateNewOrder_WhenPreviousOrderInBucketIsAlreadyPaid()
    {
        var user = await _context.Users.FirstAsync();
        var response1 = await _bulkCheckoutHandler.HandleAsync(BulkCommand());

        // Once the first order settles, a legitimate repeat of the same cart must get a fresh invoice.
        var paidOrder = await _context.Orders.FindAsync(response1.OrderIds.Single());
        paidOrder!.Status = OrderStatus.PendingFulfillment;
        await _context.SaveChangesAsync();

        var response2 = await _bulkCheckoutHandler.HandleAsync(BulkCommand());

        Assert.NotEqual(response1.OrderIds.Single(), response2.OrderIds.Single());

        var orders = await _context.Orders.Where(o => o.UserId == user.Id).ToListAsync();
        Assert.Equal(2, orders.Count);
        Assert.Equal(2, orders.Select(o => o.IdempotencyKey).Distinct().Count());
    }

    [Fact]
    public async Task CreateCheckout_ShouldThrow_WhenStationIdMissing()
    {
        var command = CheckoutCommand();
        command.StationId = null;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _createCheckoutHandler.HandleAsync(command));
        Assert.Contains("StationId", ex.Message);
    }

    [Fact]
    public async Task CreateCheckout_ShouldThrow_WhenFuelTypeNotInStation()
    {
        var command = CheckoutCommand();
        command.FuelTypeId = "unknown-fuel";

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _createCheckoutHandler.HandleAsync(command));
        Assert.Contains("Invalid fuel type", ex.Message);
    }

    // ── Slice-3 below-cost hard block (checkout safety net) ──────────────────

    [Fact]
    public async Task CreateCheckout_ShouldBlock_WhenBelowCostAndNotOptedIn()
    {
        SeedBelowCostFuel(allowBelowCost: false);

        var ex = await Assert.ThrowsAsync<BelowCostSaleBlockedException>(
            () => _createCheckoutHandler.HandleAsync(BelowCostCommand()));

        Assert.Equal("below_cost", BelowCostSaleBlockedException.Code);
        Assert.Contains("okko-loss", ex.Message);
        Assert.False(await _context.Orders.AnyAsync());
    }

    [Fact]
    public async Task CreateCheckout_ShouldSucceed_WhenBelowCostButOptedIn()
    {
        SeedBelowCostFuel(allowBelowCost: true);

        var response = await _createCheckoutHandler.HandleAsync(BelowCostCommand());

        Assert.NotEqual(Guid.Empty, response.OrderId);
        Assert.True(await _context.Orders.AnyAsync());
    }

    [Fact]
    public async Task BulkCheckout_ShouldBlock_WhenBelowCostAndNotOptedIn()
    {
        SeedBelowCostFuel(allowBelowCost: false);

        var ex = await Assert.ThrowsAsync<BelowCostSaleBlockedException>(
            () => _bulkCheckoutHandler.HandleAsync(BelowCostBulkCommand()));

        Assert.Contains("okko-loss", ex.Message);
        Assert.False(await _context.Orders.AnyAsync());
    }

    [Fact]
    public async Task BulkCheckout_ShouldSucceed_WhenBelowCostButOptedIn()
    {
        SeedBelowCostFuel(allowBelowCost: true);

        var response = await _bulkCheckoutHandler.HandleAsync(BelowCostBulkCommand());

        Assert.NotEmpty(response.OrderIds);
        Assert.True(await _context.Orders.AnyAsync());
    }

    [Fact]
    public async Task GetUserPurchases_ShouldReturnUserOrders()
    {
        var user = await _context.Users.FirstAsync();
        var order1 = BuildOrder(OrderStatus.PendingFulfillment);
        var order2 = BuildOrder(OrderStatus.Fulfilled);
        order2.FulfilledAtUtc = DateTime.UtcNow;

        _context.Orders.AddRange(order1, order2);
        await _context.SaveChangesAsync();

        var purchases = await _getUserPurchasesHandler.HandleAsync(new GetUserPurchasesCommand(user.Id));

        Assert.Equal(2, purchases.Count);
        Assert.Contains(purchases, p => p.Status == OrderStatus.Fulfilled.ToString());
        Assert.Contains(purchases, p => p.Status == OrderStatus.PendingFulfillment.ToString());
        Assert.All(purchases, p => Assert.Equal("okko", p.Provider));
        Assert.All(purchases, p => Assert.Equal("A-95", p.FuelName));
    }

    [Fact]
    public async Task GetUserPurchases_ShouldReturnEmpty_WhenUserHasNoOrders()
    {
        var user = await _context.Users.FirstAsync();
        var purchases = await _getUserPurchasesHandler.HandleAsync(new GetUserPurchasesCommand(user.Id));

        Assert.Empty(purchases);
    }

    [Fact]
    public async Task SimulatePayment_WithSuccess_ShouldMarkOrderAsPendingFulfillment()
    {
        var order = BuildOrder(OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _simulatePaymentHandler.HandleAsync(new SimulatePaymentCommand
        {
            OrderId = order.Id,
            Scenario = "success"
        });

        Assert.Equal("success", response.Status);
        Assert.NotNull(response.Purchase);

        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        Assert.Equal(OrderStatus.PendingFulfillment, updatedOrder!.Status);
        Assert.Equal(MonobankStatus.Success, updatedOrder.MonobankStatus);
    }

    [Fact]
    public async Task SimulatePayment_WithFailure_ShouldCancelOrder()
    {
        var order = BuildOrder(OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _simulatePaymentHandler.HandleAsync(new SimulatePaymentCommand
        {
            OrderId = order.Id,
            Scenario = "failure"
        });

        Assert.Equal("failed", response.Status);
        Assert.Null(response.Purchase);

        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        Assert.Equal(OrderStatus.Cancelled, updatedOrder!.Status);
        Assert.Equal(MonobankStatus.Failure, updatedOrder.MonobankStatus);
    }

    [Fact]
    public async Task SimulatePayment_ShouldThrow_WhenOrderNotFound()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _simulatePaymentHandler.HandleAsync(
            new SimulatePaymentCommand { OrderId = Guid.NewGuid(), Scenario = "success" }));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task UpdateMonobankInfo_ShouldUpdateOrderPaymentDetails()
    {
        var order = BuildOrder(OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        await _updateMonobankInfoHandler.HandleAsync(new UpdateMonobankInfoCommand
        {
            OrderId = order.Id,
            InvoiceId = "INV123456",
            Status = MonobankStatus.Success
        });

        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        Assert.Equal("INV123456", updatedOrder!.MonobankInvoiceId);
        Assert.Equal(MonobankStatus.Success, updatedOrder.MonobankStatus);
    }

    [Fact]
    public async Task UpdateMonobankInfo_ShouldThrow_WhenOrderNotFound()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _updateMonobankInfoHandler.HandleAsync(
            new UpdateMonobankInfoCommand
            {
                OrderId = Guid.NewGuid(),
                InvoiceId = "INV",
                Status = MonobankStatus.Success
            }));
    }

    private void SeedDefaultUser()
    {
        var role = new Role { Id = SeedRoles.UserRoleId, Name = SeedRoles.UserName, CreatedAtUtc = DateTime.UtcNow };
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
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.SaveChanges();
    }
}
