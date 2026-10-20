using FluentAssertions;
using FuelFlow.Features.Admin.GetRealizedMargin;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The realised margin, summed by the database.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this runs against a real Postgres.</b> The report is two grouped queries with filtered
/// aggregates over the line items and the fulfilments, and the in-memory provider evaluates the
/// expression in the client - which proves the arithmetic and nothing about the SQL. A GROUP BY
/// with a filtered SUM is exactly the shape that fails at runtime, in the money screen, on the
/// first real order.
/// </para>
/// <para>
/// The contract is the one <c>RECONCILIATION.md</c> states: the revenue is the frozen line total
/// of what was invoiced, the cost is the recorded cost of the vouchers that actually went out,
/// and the litres of vouchers that carry no cost are reported rather than folded in.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class RealizedMarginSqlIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public RealizedMarginSqlIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task The_margin_of_a_delivered_sale_is_reported()
    {
        var (orderId, fuelTypeId) = await SeedAsync(
            lineTotal: 2150m,
            deliveredCostPerLiter: 50m,
            vouchers: 1,
            liters: 50m,
            createdAtUtc: new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc));

        await using var context = CreateContext();
        var report = await new GetRealizedMarginQueryHandler(context)
            .HandleAsync(new GetRealizedMarginQuery(FromDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ToDate: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);

        report.RevenueUah.Should().Be(2150m, "the revenue is the frozen line total");
        report.CostUah.Should().Be(2500m, "the cost is what the delivered voucher cost us");
        report.NetUah.Should().Be(-350m);
        report.LossMakingLines.Should().Be(1);
        report.Lines.Should().Be(1);

        var line = report.WorstLines.Should().ContainSingle().Subject;
        line.OrderId.Should().Be(orderId);
        line.FuelTypeId.Should().Be(fuelTypeId);
        line.VouchersDelivered.Should().Be(1);
        line.LitersSold.Should().Be(50m);
        line.SupplierCostPerLiter.Should().Be(50m);
        line.UncostedLiters.Should().Be(0m);
    }

    [Fact]
    public async Task A_voucher_with_no_cost_is_reported_as_uncosted_and_carries_no_cost()
    {
        await SeedAsync(lineTotal: 2500m, deliveredCostPerLiter: null, vouchers: 1, liters: 50m,
            createdAtUtc: new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc));

        await using var context = CreateContext();
        var report = await new GetRealizedMarginQueryHandler(context)
            .HandleAsync(new GetRealizedMarginQuery(FromDate: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), ToDate: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);

        report.RevenueUah.Should().Be(2500m);
        // A voucher with no cost contributes no cost - and the litres it represents are reported,
        // because a partial truth that looks like profit is the dangerous direction.
        report.CostUah.Should().Be(0m);
        report.UncostedLiters.Should().Be(50m);

        report.WorstLines.Should().BeEmpty();
    }

    [Fact]
    public async Task A_line_with_no_voucher_delivered_earns_no_margin()
    {
        await SeedAsync(
            lineTotal: 2500m,
            deliveredCostPerLiter: 50m,
            vouchers: 0,
            liters: 50m,
            createdAtUtc: new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc));

        await using var context = CreateContext();
        var report = await new GetRealizedMarginQueryHandler(context)
            .HandleAsync(new GetRealizedMarginQuery(FromDate: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), ToDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);

        report.RevenueUah.Should().Be(0m, "a group with no voucher handed over earns no margin");
        report.Lines.Should().Be(0);
    }

    private async Task<(Guid OrderId, Guid FuelTypeId)> SeedAsync(
        decimal lineTotal,
        decimal? deliveredCostPerLiter,
        int vouchers,
        decimal liters,
        DateTime createdAtUtc)
    {
        var userId = Guid.NewGuid();
        var fuelTypeId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using var context = CreateContext();

        var brandId = await context.Brands.Where(b => b.Slug == "okko").Select(b => b.Id).FirstAsync();

        context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{Guid.NewGuid():N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = fuelTypeId,
            Name = "Margin " + Guid.NewGuid().ToString("N")[..8],
            BrandId = brandId,
            BasePrice = 50m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = lineTotal,
            Status = OrderStatus.Fulfilled,
            Kind = OrderKind.Purchase,
            FulfilledAtUtc = DateTime.UtcNow.AddDays(-2),
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = DateTime.UtcNow
        };

        order.LineItems.Add(new OrderLineItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Brand = "okko",
            FuelTypeId = fuelTypeId,
            Liters = liters,
            Quantity = Math.Max(vouchers, 1),
            UnitPrice = lineTotal / Math.Max(vouchers, 1),
            LineTotal = lineTotal
        });

        context.Orders.Add(order);

        foreach (var _ in Enumerable.Range(0, vouchers))
        {
            var voucher = new FuelVoucher
            {
                Id = Guid.NewGuid(),
                Brand = "okko",
                FuelTypeId = fuelTypeId,
                Liters = liters,
                CostPerLiter = deliveredCostPerLiter,
                VoucherNumber = Guid.NewGuid().ToString("N"),
                QrPayload = Guid.NewGuid().ToString("N"),
                Status = VoucherStatus.Assigned,
                // An assigned voucher is held against an order - the schema refuses one without it.
                OrderId = orderId,
                SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(90),
                CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(90),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            context.FuelVouchers.Add(voucher);

            context.Fulfillments.Add(new Fulfillment
            {
                // No id: the column is an identity, and the database gives it out. The unit test
                // has to set one, because the in-memory provider has no identity to ask.
                OrderId = orderId,
                VoucherId = voucher.Id,
                FulfilledAtUtc = DateTime.UtcNow.AddDays(-2)
            });
        }

        await context.SaveChangesAsync();
        return (orderId, fuelTypeId);
    }
}
