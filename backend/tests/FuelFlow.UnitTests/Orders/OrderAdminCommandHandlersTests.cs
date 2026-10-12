using FuelFlow.Features.Settings;
using FluentAssertions;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.DeleteOrder;
using FuelFlow.Features.Orders.GetAdminPurchases;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

using FuelFlow.SharedKernel.Observability;
namespace FuelFlow.UnitTests.Orders;

public sealed class OrderAdminCommandHandlersTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IMonobankClient> _monobankClientMock;
    private readonly BulkCheckoutCommandHandler _bulkCheckoutHandler;
    private readonly DeleteOrderCommandHandler _deleteOrderHandler;
    private readonly GetAdminPurchasesQueryHandler _getAdminPurchasesHandler;

    public OrderAdminCommandHandlersTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
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
            NotificationDispatcher.Disabled);

        _deleteOrderHandler = new DeleteOrderCommandHandler(_context);
        _getAdminPurchasesHandler = new GetAdminPurchasesQueryHandler(_context);
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
            },
            new FuelPackage
            {
                Id = "pkg-okko-p95-50",
                StationId = "okko",
                FuelTypeId = "okko-p95",
                FuelName = "Pulls 95",
                Liters = 50m,
                Price = 2900,
                OriginalPrice = 2850,
                SupplierPricePerLiter = 57m,
                MarginUahPerLiter = 1m,
                FinalPricePerLiter = 58m,
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

    private static Order BuildOrder(Guid userId, OrderStatus status = OrderStatus.PendingPayment) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Price = 2500,
        Status = status,
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

    private static CheckoutItem CheckoutItem(string fuelTypeId, int price, string? stationId = "okko") => new()
    {
        Provider = "okko",
        FuelTypeId = fuelTypeId,
        Liters = 50,
        Quantity = 1,
        Price = price,
        StationId = stationId,
        StationName = "OKKO"
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
    public async Task BulkCheckout_ShouldCreateSingleOrder_WithAllLineItems_AndSharedInvoice()
    {
        var user = await _context.Users.FirstAsync();
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
        order.LineItems.Should().Contain(li => li.FuelTypeId == "okko-95");
        order.LineItems.Should().Contain(li => li.FuelTypeId == "okko-p95");
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
    public async Task GetAdminPurchases_ShouldReturnOrders_WithVoucherCount()
    {
        var order = BuildOrder(Guid.NewGuid(), OrderStatus.Fulfilled);
        order.FulfilledAtUtc = DateTime.UtcNow;
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 1,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow,
            Voucher = new FuelVoucher
            {
                Id = Guid.NewGuid(),
                Provider = "okko",
                FuelTypeId = "okko-95",
                Liters = 50,
                VoucherNumber = "V-test",
                QrPayload = "q",
                ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            }
        });

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _getAdminPurchasesHandler.HandleAsync(new GetAdminPurchasesQuery());

        result.Should().ContainSingle();
        result[0].Id.Should().Be(order.Id);
        result[0].VoucherCount.Should().Be(1);
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

