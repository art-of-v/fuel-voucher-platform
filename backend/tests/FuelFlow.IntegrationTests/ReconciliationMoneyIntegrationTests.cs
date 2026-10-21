using FluentAssertions;
using FuelFlow.Features.Admin.GetReconciliation;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The reconciliation's money is the database's arithmetic, and it is the same number it was.
/// </summary>
/// <remarks>
/// <para>
/// The revenue, the received total, the delivered value and the refunded total used to be
/// added up in C# over every closed order in the platform - every order with its line items,
/// its fulfillments and its vouchers - and the summary the screen shows was the sum over
/// whatever the process happened to be holding. They are aggregates over the whole history,
/// and they are now the database's sums.
/// </para>
/// <para>
/// <b>Why the expectations here are written out by hand.</b> A test that recomputes the
/// numbers the way the code used to would only prove that the code and its copy agree. These
/// are four orders with a stated margin, a stated price and a stated number of deliveries,
/// and the expected figures are counted from them: the revenue is the margin times the
/// litres times the lesser of what was ordered and what was delivered, and the delivered value
/// is what the order was worth less what the customer never received.
/// </para>
/// <para>
/// <b>Why it is a database test.</b> The sums are correlated subqueries and grouped joins,
/// and the in-memory provider answers them differently. Only a real PostgreSQL says whether
/// the plan is the one the money assumes.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class ReconciliationMoneyIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private const decimal MarginUahPerLiter = 10m;
    private const decimal Liters = 50m;

    private readonly TestDatabaseFixture _fixture;

    public ReconciliationMoneyIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_Money_Is_The_Same_Number_It_Was()
    {
        await using var context = CreateContext();
        await SeedAsync(context);

        var handler = new GetReconciliationQueryHandler(context);
        var response = await handler.HandleAsync(new GetReconciliationQuery());

        // Order A: 2 units at 1000 UAH, ONE delivery. Order B: 1 unit at 500 UAH, TWO
        // deliveries. Order C: 1 unit at 100 UAH, never paid.
        //
        // REVENUE - the margin (10) times the litres (50) times the lesser of what was
        // ordered and what was delivered: A earns 10 x 50 x 1 = 500, B earns 10 x 50 x 1 =
        // 500, C earns nothing. 1 000 in total.
        //
        // The number is the one the C# produced, unchanged - and it is UAH, and it says so:
        // the margin is UAH per litre, so the product is UAH. The field is called
        // `TotalRevenueUah`; it used to say "kopecks" while holding UAH, which made the
        // screen and the field disagree about the unit of the same number. The number did
        // not move, and it does not move here.
        response.Summary.TotalRevenueUah.Should().Be(1_000m);

        // RECEIVED - what the brand confirmed it took: A's price (2 000 UAH) and B's price
        // (500 UAH). C was never paid, so it is not received.
        response.Summary.TotalReceivedKopecks.Should().Be(250_000m);

        // DELIVERED VALUE - what each order was worth, less what the customer never received:
        // A is worth 2 000 UAH and one unit of it was never delivered (1 000 UAH), so A
        // delivers 1 000 UAH; B is worth 500 UAH and both its units arrived, so B delivers
        // 500 UAH. C was never paid, so it is worth nothing.
        response.Summary.TotalFulfilledValueKopecks.Should().Be(150_000m);

        // REFUNDED - the successful refund against order A, and nothing else.
        response.Summary.TotalRefundedKopecks.Should().Be(30_000m);

        // The order count is the number of closed orders, whether or not they earned a margin.
        response.Summary.RefundedOrders.Should().Be(0,
            "a refund payment does not make an order refunded; the order's own status does");

        // The monthly summary carries the same revenue, with the closed orders beside it.
        var month = response.RevenueSummary.Should().ContainSingle().Subject;
        month.OrderCount.Should().Be(2, "two orders are closed, and both are in the month");
        month.RevenueUah.Should().Be(1_000m, "the same revenue the total carries");
    }

    private async Task SeedAsync(ApplicationDbContext context)
    {
        await context.Database.MigrateAsync();

        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

        var userId = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var brand = await context.Brands.AsNoTracking().SingleAsync(b => b.Slug == "okko");

        // A fuel of our own, so the margin is a number this test states rather than one it
        // reads from the seed.
        var fuelId = Guid.NewGuid();
        context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = fuelId,
            BrandId = brand.Id,
            Name = $"Reconciliation test fuel {Guid.NewGuid():N}",
            BrandFuelCode = $"recon-test-{Guid.NewGuid():N}",
            BasePrice = 1200m,
            DiscountPrice = 1000m,
            MarginUahPerLiter = MarginUahPerLiter,
            PumpPricePerLiter = 1200m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        context.FuelNominals.Add(new FuelNominal
        {
            FuelTypeId = fuelId,
            Liters = Liters,
            DataKind = DataKinds.Real,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var createdAt = new DateTime(2026, 3, 4, 9, 0, 0, DateTimeKind.Utc);

        // Order A: two units ordered, one delivered.
        var orderA = AddOrder(context, userId, fuelId, brand.Slug, createdAt,
            quantity: 2, unitPrice: 1000m, paid: true);
        AddDelivery(context, orderA, fuelId, brand.Slug, createdAt);

        // Order B: one unit ordered, two delivered.
        var orderB = AddOrder(context, userId, fuelId, brand.Slug, createdAt.AddMinutes(1),
            quantity: 1, unitPrice: 500m, paid: true);
        AddDelivery(context, orderB, fuelId, brand.Slug, createdAt.AddMinutes(1));
        AddDelivery(context, orderB, fuelId, brand.Slug, createdAt.AddMinutes(1));

        // Order C: never paid, so it earns nothing and was received for nothing.
        AddOrder(context, userId, fuelId, brand.Slug, createdAt.AddMinutes(2),
            quantity: 1, unitPrice: 100m, paid: false);

        // The capture order A paid for, and the successful refund against it. A refund is
        // defined by what it reverses, so the capture has to exist.
        var captureId = Guid.NewGuid();
        context.Payments.Add(new Payment
        {
            Id = captureId,
            OrderId = orderA.Id,
            Kind = PaymentKind.Capture,
            Status = PaymentStatus.Succeeded,
            Provider = "monobank",
            AmountUah = 2000m,
            AmountKopecks = 200_000,
            PaidAtUtc = createdAt.AddMinutes(5),
            CreatedAtUtc = createdAt.AddMinutes(5),
            UpdatedAtUtc = createdAt.AddMinutes(5)
        });

        context.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderA.Id,
            ParentPaymentId = captureId,
            Kind = PaymentKind.Refund,
            Provider = "monobank",
            Status = PaymentStatus.Succeeded,
            AmountUah = 300m,
            AmountKopecks = 30_000,
            PaidAtUtc = createdAt.AddMinutes(6),
            CreatedAtUtc = createdAt.AddMinutes(5),
            UpdatedAtUtc = createdAt.AddMinutes(5)
        });

        await context.SaveChangesAsync();
    }

    private static Order AddOrder(
        ApplicationDbContext context,
        Guid userId,
        Guid fuelId,
        string brandSlug,
        DateTime createdAt,
        int quantity,
        decimal unitPrice,
        bool paid)
    {
        var orderId = Guid.NewGuid();
        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = unitPrice * quantity,
            Status = paid ? OrderStatus.Fulfilled : OrderStatus.PendingPayment,
            MonobankStatus = paid ? MonobankStatus.Success : null,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt,
            FulfilledAtUtc = paid ? createdAt : null,
            LineItems = new List<OrderLineItem>
            {
                new OrderLineItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    BrandId = BrandIds.ForSlug(brandSlug),
                    Brand = brandSlug,
                    FuelTypeId = fuelId,
                    Liters = Liters,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    LineTotal = unitPrice * quantity
                }
            }
        };

        context.Orders.Add(order);
        return order;
    }

    private static void AddDelivery(
        ApplicationDbContext context,
        Order order,
        Guid fuelId,
        string brandSlug,
        DateTime createdAt)
    {
        var voucherId = Guid.NewGuid();
        context.FuelVouchers.Add(new FuelVoucher
        {
            Id = voucherId,
            OrderId = order.Id,
            BrandId = BrandIds.ForSlug(brandSlug),
            Brand = brandSlug,
            FuelTypeId = fuelId,
            Liters = Liters,
            VoucherNumber = $"recon-{voucherId:N}"[..20],
            QrPayload = $"qr-{voucherId:N}",
            Status = VoucherStatus.Assigned,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt
        });

        context.Fulfillments.Add(new Fulfillment
        {
            OrderId = order.Id,
            VoucherId = voucherId,
            FulfilledAtUtc = createdAt
        });
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
