using FluentAssertions;
using FuelFlow.Features.Admin.GetRealizedMargin;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Admin;

/// <summary>
/// The detective half of loss prevention. The guards stop a specific route to a below-cost sale; this
/// measures whether money actually was lost, from the two numbers that cannot be argued with: the
/// frozen invoice amount and the cost recorded on the voucher that really went out.
/// </summary>
public sealed class GetRealizedMarginQueryHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public GetRealizedMarginQueryHandlerTests()
    {
        _context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private GetRealizedMarginQueryHandler Handler() => new(_context);

    /// <summary>
    /// One order, one line, and <paramref name="deliveredCostPerLiter"/> on the voucher that is
    /// actually handed over — the whole point being that it can differ from the blended price the
    /// customer was charged.
    /// </summary>
private async Task<Order> SeedAsync(
        decimal lineTotal,
        decimal deliveredCostPerLiter,
        int vouchers = 1,
        decimal liters = 50m,
        string fuelTypeId = "okko-95",
        OrderStatus status = OrderStatus.Fulfilled)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Status = status,
            Kind = OrderKind.Purchase,
            Price = lineTotal,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            UpdatedAtUtc = DateTime.UtcNow,
        };
        order.LineItems.Add(new OrderLineItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Provider = "okko",
            FuelTypeId = fuelTypeId,
            Liters = liters,
            Quantity = vouchers,
            UnitPrice = lineTotal / vouchers,
            LineTotal = lineTotal,
        });
        _context.Orders.Add(order);

        var fulfillmentId = 0;
        foreach (var _ in Enumerable.Range(0, vouchers))
        {
            var voucher = new FuelVoucher
            {
                Id = Guid.NewGuid(),
                Provider = "okko",
                FuelTypeId = fuelTypeId,
                Liters = liters,
                CostPerLiter = deliveredCostPerLiter,
                VoucherNumber = Guid.NewGuid().ToString("N"),
                QrPayload = $"9015$2000$;{fuelTypeId}={vouchers}?",
                Status = VoucherStatus.Assigned,
                ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(90),
                CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(90),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            _context.FuelVouchers.Add(voucher);
            // int identity, so it must be unique across the whole context, not per order.
            order.Fulfillments.Add(new Fulfillment
            {
                Id = Interlocked.Increment(ref _nextFulfillmentId),
                OrderId = order.Id,
                VoucherId = voucher.Id,
                Voucher = voucher,
                FulfilledAtUtc = DateTime.UtcNow.AddDays(-2),
            });
        }

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return order;
    }

    private int _nextFulfillmentId = 1000;

    [Fact]
    public async Task ShouldReportALoss_WhenAnExpensiveVoucherIsSoldAtTheBlendedPrice()
    {
        // The scenario the whole report exists for. The price was NOT wrong: 2150 is exactly
        // 43/litre × 50, which is what the blended cost of 41 + margin 2 legitimately produces. But the
        // voucher that left the door cost 50/litre, so 2150 - 2500 is a real 350 UAH loss. No guard can
        // see this coming — the blend was correct and the pump was nowhere near below cost.
        await SeedAsync(lineTotal: 2150m, deliveredCostPerLiter: 50m);

        var report = await Handler().HandleAsync(new GetRealizedMarginQuery());

        report.RevenueUah.Should().Be(2150m);
        report.CostUah.Should().Be(2500m);
        report.NetUah.Should().Be(-350m);
        report.LossMakingLines.Should().Be(1);

        var line = report.WorstLines.Should().ContainSingle().Subject;
        line.FuelTypeId.Should().Be("okko-95");
        line.SupplierCostPerLiter.Should().Be(50m);
        line.IsLossMaking.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldNotReportALoss_WhenTheDeliveredVoucherIsCheaperThanWeSoldFor()
    {
        await SeedAsync(lineTotal: 2500m, deliveredCostPerLiter: 45m);

        var report = await Handler().HandleAsync(new GetRealizedMarginQuery());

        report.NetUah.Should().Be(250m);
        report.LossMakingLines.Should().Be(0);
        report.WorstLines.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldNotReportALoss_OnAnExactlyBreakEvenSale()
    {
        // Equal is not a loss. A deliberate zero-margin release of stock is a real tactic and must not
        // be reported as one.
        await SeedAsync(lineTotal: 2500m, deliveredCostPerLiter: 50m);

        var report = await Handler().HandleAsync(new GetRealizedMarginQuery());

        report.NetUah.Should().Be(0m);
        report.LossMakingLines.Should().Be(0);
    }

    [Fact]
    public async Task ShouldSurfaceUncostedStock_RatherThanFoldingItIntoAnOptimisticProfit()
    {
        // The voucher's cost was never recorded, so the true cost is unknown and certainly not above
        // zero. Reporting revenue minus zero would manufacture profit out of missing data.
        await SeedAsync(lineTotal: 2150m, deliveredCostPerLiter: 0m);

        var report = await Handler().HandleAsync(new GetRealizedMarginQuery());

        report.UncostedLiters.Should().Be(50m);
        report.LossMakingLines.Should().Be(0, "a line with unknown cost cannot be called a loss");
        report.NetUah.Should().Be(2150m, "cost is honestly zero here, and the gap is reported beside it");
    }

    [Fact]
    public async Task ShouldIgnoreLinesThatReachedNoFulfillment()
    {
        // Paid but not yet delivered: no voucher, so no cost to compare. Revenue is only counted for
        // lines that actually reached a customer.
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Status = OrderStatus.Paid,
            Kind = OrderKind.Purchase,
            Price = 1000m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        order.LineItems.Add(new OrderLineItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Provider = "okko",
            FuelTypeId = "okko-95",
            Liters = 50,
            Quantity = 1,
            UnitPrice = 1000m,
            LineTotal = 1000m,
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var report = await Handler().HandleAsync(new GetRealizedMarginQuery());

        report.Lines.Should().Be(0);
        report.RevenueUah.Should().Be(0m);
    }

    [Fact]
    public async Task ShouldExcludeCompanyHandovers()
    {
// A handover never earned revenue, so comparing its price against a voucher's cost would
        // invent a loss out of a transfer.
        var order = await SeedAsync(lineTotal: 2500m, deliveredCostPerLiter: 60m);
        // SeedAsync clears the tracker, so the order is detached. Re-attach it as Modified rather than
        // mutating the instance, which would silently change nothing.
        _context.Orders.Attach(order);
        order.Kind = OrderKind.ReceivedFromCompany;
        _context.Orders.Update(order);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var report = await Handler().HandleAsync(new GetRealizedMarginQuery());

        report.Lines.Should().Be(0);
        report.LossMakingLines.Should().Be(0);
    }

    [Fact]
    public async Task ShouldExcludeOrdersThatWereNeverPaid()
    {
        await SeedAsync(lineTotal: 2500m, deliveredCostPerLiter: 60m, status: OrderStatus.Cancelled);

        var report = await Handler().HandleAsync(new GetRealizedMarginQuery());

        report.Lines.Should().Be(0);
    }

    [Fact]
    public async Task ShouldListLossLinesWorstFirst()
    {
        await SeedAsync(lineTotal: 1000m, deliveredCostPerLiter: 60m, fuelTypeId: "okko-gas");
        await SeedAsync(lineTotal: 2000m, deliveredCostPerLiter: 45m, fuelTypeId: "okko-dp");

        var report = await Handler().HandleAsync(new GetRealizedMarginQuery());

        report.LossMakingLines.Should().Be(2);
        // gas: 1000 - 3000 = -2000 (worse). diesel: 2000 - 2250 = -250.
        report.WorstLines.Select(l => l.NetUah).Should().ContainInOrder(-2000m, -250m);
    }

    [Fact]
    public async Task ShouldHonourTheDateFilter()
    {
var old = await SeedAsync(lineTotal: 1000m, deliveredCostPerLiter: 60m);
        _context.Orders.Attach(old);
        old.CreatedAtUtc = DateTime.UtcNow.AddDays(-90);
        _context.Orders.Update(old);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var recent = await Handler().HandleAsync(new GetRealizedMarginQuery(DateTime.UtcNow.AddDays(-7)));
        var all = await Handler().HandleAsync(new GetRealizedMarginQuery());

        recent.Lines.Should().Be(0);
        all.Lines.Should().Be(1);
    }

    [Fact]
    public async Task ShouldCountEveryVoucherOfAMultiVoucherOrder()
    {
        await SeedAsync(lineTotal: 4300m, deliveredCostPerLiter: 50m, vouchers: 2);

        var report = await Handler().HandleAsync(new GetRealizedMarginQuery());

        var line = report.WorstLines.Should().ContainSingle().Subject;
        line.VouchersDelivered.Should().Be(2);
        line.CostUah.Should().Be(5000m);   // 2 × 50 L × 50 UAH
        line.NetUah.Should().Be(-700m);
    }
}
