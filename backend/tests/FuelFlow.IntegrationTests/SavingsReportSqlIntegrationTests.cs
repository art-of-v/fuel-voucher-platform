using FluentAssertions;
using FuelFlow.Features.Orders.GetSavingsReport;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The customer's savings report, summed by the database.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this runs against a real Postgres.</b> The report is a GROUP BY over the line items
/// joined to the orders, with the month taken apart into its year and its month, the saving
/// clamped with GREATEST and the missing pump reference folded in with a COALESCE. The
/// in-memory provider evaluates all of that in the client, which proves the arithmetic and
/// nothing about the SQL - and a translation that fails here fails in the customer's own
/// savings screen.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class SavingsReportSqlIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public SavingsReportSqlIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task The_saving_is_the_pump_difference_and_never_negative()
    {
        var userId = Guid.NewGuid();
        var fuelTypeId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            var brandId = await context.Brands.Where(b => b.Slug == "okko").Select(b => b.Id).FirstAsync();

            context.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = $"+38{Guid.NewGuid():N}"[..20],
                IsActive = true,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });

            context.FuelTypes.Add(new FuelTypeEntity
            {
                Id = fuelTypeId,
                Name = "Savings " + Guid.NewGuid().ToString("N")[..8],
                BrandId = brandId,
                BasePrice = 50m,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });

            // Bought 100 L for 2200, the pump would have been 2500 -> 300 saved.
            var order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Price = 2200m,
                Status = OrderStatus.Fulfilled,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            };

            order.LineItems.Add(new OrderLineItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = fuelTypeId,
                Liters = 100m,
                Quantity = 1,
                UnitPrice = 2200m,
                LineTotal = 2200m,
                OriginalLineTotal = 2500m
            });

            context.Orders.Add(order);

            var voucher = new FuelVoucher
            {
                Id = Guid.NewGuid(),
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = fuelTypeId,
                Liters = 100m,
                VoucherNumber = Guid.NewGuid().ToString("N"),
                QrPayload = Guid.NewGuid().ToString("N"),
                Status = VoucherStatus.Assigned,
                // An assigned voucher is held against an order - the schema refuses one without it.
                OrderId = order.Id,
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            };
            context.FuelVouchers.Add(voucher);
            context.Fulfillments.Add(new Fulfillment
            {
                OrderId = order.Id,
                VoucherId = voucher.Id,
                FulfilledAtUtc = TestClock.UtcNow
            });

            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var report = await new GetSavingsReportQueryHandler(verify)
            .HandleAsync(new GetSavingsReportQuery(userId), CancellationToken.None);

        report.OrdersCount.Should().Be(1);
        report.TotalPaid.Should().Be(2200m);
        report.TotalLiters.Should().Be(100m);
        report.TotalSavings.Should().Be(300m, "the saving is the pump difference");

        report.Monthly.Should().ContainSingle();
        report.Monthly[0].Paid.Should().Be(2200m);
        report.Monthly[0].Saved.Should().Be(300m);
        report.Monthly[0].Liters.Should().Be(100m);

        report.RemainingVouchers.Should().Be(1, "the assigned voucher is still the customer's");
        report.RemainingLiters.Should().Be(100m);
    }
}
