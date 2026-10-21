using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// A fulfilment used to say that an order delivered <i>a</i> voucher, never which line of the order it
/// satisfied. The reconciliation filled that in by grouping both sides by litres — and litres cannot
/// tell two lines of the same volume apart.
/// </summary>
/// <remarks>
/// <para>
/// The case this suite exists for: an order for <c>OKKO A95 10 L ×2</c> and <c>WOG A95 10 L ×1</c>
/// that received three OKKO vouchers and no WOG voucher. Both sides group to <c>10 L: 3</c>, the
/// mismatch check passes, and the customer is short a line with the order reading as fulfilled. Worse,
/// the repair released the excess out of the same litres bucket, so on any order where the totals did
/// not line up it had no way to tell which delivery was the wrong one.
/// </para>
/// <para>
/// Each test builds the two line items explicitly rather than through checkout, because the point is
/// what the reconciliation does with a given set of rows — not that some other feature produced them.
/// The production default tracking behaviour is kept (no <c>NoTracking</c>), for the reason
/// <see cref="FulfillmentMismatchRepairTests"/> sets out at length.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class FulfillmentLineAttributionTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FulfillmentLineAttributionTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// The bug. Two lines, same volume, different brand. Three vouchers delivered, but all of them
    /// the wrong fuel for the second line — so litres say "3 delivered, 3 wanted" and pass.
    /// </summary>
    [Fact]
    public async Task AShortLine_IsDetected_WhenAnotherLineOfTheSameVolumeWasOverDelivered()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var okkoLine = Guid.NewGuid();
        var wogLine = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetDataAsync(seed);

            seed.Users.Add(User(userId));
            AddPaidOrder(seed, TwoLineOrder(orderId, userId, okkoLine, wogLine));

            // Three OKKO vouchers for a line that wanted two, and nothing at all for the WOG line.
            AddVoucherWithFulfilment(seed, orderId, okkoLine, userId, "okko-95", "A");
            AddVoucherWithFulfilment(seed, orderId, okkoLine, userId, "okko-95", "B");
            AddVoucherWithFulfilment(seed, orderId, okkoLine, userId, "okko-95", "C");

            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        await CreateService(context).ProcessPendingOrdersAsync();

        // The whole point: litres would have passed this. Per-line, the WOG line has nothing.
        await using var verify = CreateContext();
        var line = await verify.Fulfillments
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(f => f.VoucherId != Guid.Empty && f.LineItemId == okkoLine && f.ReleasedAtUtc != null);

        line.Should().NotBeNull("the excess belongs to the OKKO line and only the OKKO line may be released");

        var stillStanding = await verify.Fulfillments.CountAsync();
        stillStanding.Should().Be(2, "two OKKO vouchers for a line that asked for two");
    }

    /// <summary>
    /// The counterweight: a correctly delivered order must be left completely alone. A check that
    /// fires on healthy data is worse than no check, because the repair takes fuel from customers.
    /// </summary>
    [Fact]
    public async Task ACorrectlyDeliveredOrder_IsNotTouched()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var okkoLine = Guid.NewGuid();
        var wogLine = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetDataAsync(seed);

            seed.Users.Add(User(userId));
            AddPaidOrder(seed, TwoLineOrder(orderId, userId, okkoLine, wogLine));
            AddVoucherWithFulfilment(seed, orderId, okkoLine, userId, "okko-95", "A");
            AddVoucherWithFulfilment(seed, orderId, okkoLine, userId, "okko-95", "B");
            AddVoucherWithFulfilment(seed, orderId, wogLine, userId, "wog-95", "C");

            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        await CreateService(context).ProcessPendingOrdersAsync();

        await using var verify = CreateContext();
        (await verify.Fulfillments.CountAsync()).Should().Be(3, "every line got exactly what it asked for");
        (await verify.Fulfillments.IgnoreQueryFilters().CountAsync(f => f.ReleasedAtUtc != null))
            .Should().Be(0, "nothing may be released from a correct order");
    }

    /// <summary>
    /// Rows that predate the column keep the litres comparison. Without this the partial backfill
    /// would turn every legacy order into a false mismatch, and the repair would strip fuel from
    /// customers whose deliveries were fine.
    /// </summary>
    [Fact]
    public async Task AnOrderWithUnattributedFulfillments_FallsBackToTheLitresComparison()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetDataAsync(seed);

            seed.Users.Add(User(userId));

            var order = SingleLineOrder(orderId, userId, lineId, quantity: 2);
            order.Status = OrderStatus.Fulfilled;
            order.FulfilledAtUtc = TestClock.UtcNow;
            AddPaidOrder(seed, order);

            // Right count, no line named: the legacy shape. Under litres this is correct, and the
            // order is old enough that the repair would decline it anyway - both are asserted here.
            AddUnattributedVoucherWithFulfilment(seed, orderId, userId, "okko-95", "A");
            AddUnattributedVoucherWithFulfilment(seed, orderId, userId, "okko-95", "B");

            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        await CreateService(context).ProcessPendingOrdersAsync();

        await using var verify = CreateContext();
        (await verify.Fulfillments.CountAsync()).Should().Be(2);
        (await verify.Fulfillments.IgnoreQueryFilters().CountAsync(f => f.ReleasedAtUtc != null))
            .Should().Be(0, "two vouchers for a line that wanted two is not a mismatch, attributed or not");
    }

    /// <summary>
    /// The backfill's own rule: attribute what is unambiguous, leave the rest. Asserted through the
    /// migration rather than through a handler, because the rule lives in SQL.
    /// </summary>
    [Fact]
    public async Task TheBackfill_AttributesOnlyUnambiguousRows()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        // The columns must exist and be nullable: a non-null column would have failed the migration
        // outright on a table with existing rows, which is the proof they were nullable all along.
        var fulfillments = await context.Database.SqlQueryRaw<string>(
            """
            SELECT is_nullable FROM information_schema.columns
             WHERE table_name = 'fulfillments' AND column_name = 'line_item_id'
            """).ToListAsync();

        fulfillments.Should().ContainSingle().Which.Should().Be("YES");
    }

    // ---------------------------------------------------------------- fixtures

    private static Order TwoLineOrder(Guid orderId, Guid userId, Guid okkoLine, Guid wogLine) => new()
    {
        Id = orderId,
        UserId = userId,
        Price = 7500m,
        Status = OrderStatus.Fulfilled,
        FulfilledAtUtc = TestClock.UtcNow,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow,
        LineItems = new List<OrderLineItem>
        {
            Line(okkoLine, orderId, "okko", "okko-95", 10m, quantity: 2, unitPrice: 2500m),
            Line(wogLine, orderId, "wog", "wog-95", 10m, quantity: 1, unitPrice: 2500m)
        }
    };

    private static Order SingleLineOrder(Guid orderId, Guid userId, Guid lineId, int quantity) => new()
    {
        Id = orderId,
        UserId = userId,
        Price = quantity * 2500m,
        Status = OrderStatus.PendingFulfillment,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow,
        LineItems = new List<OrderLineItem>
        {
            Line(lineId, orderId, "okko", "okko-95", 10m, quantity, 2500m)
        }
    };

    private static OrderLineItem Line(
        Guid id, Guid orderId, string brand, string fuelTypeId, decimal liters, int quantity, decimal unitPrice) => new()
    {
        Id = id,
        OrderId = orderId,
        BrandId = BrandIds.ForSlug(brand),
        Brand = brand,
        FuelTypeId =   ToUuid(fuelTypeId),
        Liters = liters,
        Quantity = quantity,
        UnitPrice = unitPrice,
        LineTotal = unitPrice * quantity
    };

    private static void AddVoucherWithFulfilment(
        ApplicationDbContext context, Guid orderId, Guid lineId, Guid userId, string fuelTypeId, string tag)
    {
        var voucher = Voucher(userId, orderId, fuelTypeId, 10m, tag);
        context.FuelVouchers.Add(voucher);
        context.Fulfillments.Add(Attributed(orderId, lineId, voucher));
    }

    private static void AddUnattributedVoucherWithFulfilment(
        ApplicationDbContext context, Guid orderId, Guid userId, string fuelTypeId, string tag)
    {
        var voucher = Voucher(userId, orderId, fuelTypeId, 10m, tag);
        context.FuelVouchers.Add(voucher);
        context.Fulfillments.Add(Unattributed(orderId, voucher));
    }

    private static Fulfillment Attributed(Guid orderId, Guid lineId, FuelVoucher voucher) => new()
    {
        OrderId = orderId,
        VoucherId = voucher.Id,
        LineItemId = lineId,
        FulfilledAtUtc = voucher.UpdatedAtUtc
    };

    private static Fulfillment Unattributed(Guid orderId, FuelVoucher voucher) => new()
    {
        OrderId = orderId,
        VoucherId = voucher.Id,
        LineItemId = null,
        FulfilledAtUtc = voucher.UpdatedAtUtc
    };

    private static FuelVoucher Voucher(Guid userId, Guid orderId, string fuelTypeId, decimal liters, string tag)
    {
        var nonce = $"{tag}-{Guid.NewGuid():N}";
        var brand = fuelTypeId.StartsWith("wog", StringComparison.Ordinal) ? "wog" : "okko";

        return new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Brand = brand,
            BrandId = BrandIds.ForSlug(brand),
            FuelTypeId =   ToUuid(fuelTypeId),
            Liters = liters,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
            VoucherNumber = nonce,
            QrPayload = nonce,
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            OrderId = orderId,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };
    }

    private static User User(Guid id) => new()
    {
        Id = id,
        PhoneNumber = $"+38{id:N}"[..20],
        IsActive = true,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    private static async Task ResetDataAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private FulfillmentService CreateService(ApplicationDbContext context)
    {
        var refundHandler = new RefundOrderCommandHandler(
            context,
            new StubMonobankClientFactory(new Mock<IMonobankClient>().Object),
            new AuditEventService(context));

        return new FulfillmentService(
            context,
            NullLogger<FulfillmentService>.Instance,
            refundHandler,
            new RuntimeSettingsService(context),
            NotificationDispatcher.Disabled,
            new ConfigurationBuilder().Build());
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
