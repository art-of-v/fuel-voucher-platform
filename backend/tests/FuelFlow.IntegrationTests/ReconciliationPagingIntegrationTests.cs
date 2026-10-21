using FluentAssertions;
using FuelFlow.Features.Admin.GetReconciliation;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The reconciliation's three-way match is a page, and its exceptions are not the page's.
/// </summary>
/// <remarks>
/// <para>
/// The three-way match is a row per order, so it grows with every order the shop has ever
/// taken. It used to be answered with all of them - each with its fulfillments, its vouchers
/// and its line items - on a 4 GB box with 1 GB of Postgres.
/// </para>
/// <para>
/// <b>Why the exceptions are a separate thing.</b> The issues panel is the operator's "what
/// needs attention" list, and it must not be the page's. An order that was never fulfilled is
/// usually an OLD order, so a page-scoped list would be empty exactly when there is something
/// to look at. The test below puts the unfulfilled order on the LAST page and asserts that its
/// exception is still raised.
/// </para>
/// <para>
/// <b>Why it is a database test.</b> The page is taken by a SQL <c>OFFSET</c> over an ordered
/// query, and the order needs a tie-break: two orders created in the same tick would otherwise
/// swap places between two pages and one of them would be shown twice. The in-memory provider
/// does not order the way Postgres does, so only a real database can say whether the page
/// holds.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class ReconciliationPagingIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ReconciliationPagingIntegrationTests(TestDatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task The_Three_Way_Match_Is_A_Page_And_The_Exceptions_Are_Not_The_Page_S()
    {
        await using var context = CreateContext();
        await SeedAsync(context);

        var handler = new GetReconciliationQueryHandler(context);

        var firstPage = await handler.HandleAsync(new GetReconciliationQuery(Page: 1, Limit: 1));

        firstPage.ThreeWayMatch.TotalCount.Should().Be(3, "the count says how many there are, not how many came back");
        firstPage.ThreeWayMatch.Items.Should().HaveCount(1, "the page holds what was asked for");
        firstPage.ThreeWayMatch.Page.Should().Be(1);
        firstPage.ThreeWayMatch.PageSize.Should().Be(1);
        firstPage.ThreeWayMatch.TotalPages.Should().Be(3);
        firstPage.ThreeWayMatch.HasPreviousPage.Should().BeFalse();
        firstPage.ThreeWayMatch.HasNextPage.Should().BeTrue();

        // The page is the newest first, so the unfulfilled order - the OLD one - is not on it.
        firstPage.ThreeWayMatch.Items.Should().NotContain(t => t.MatchStatus == "UNFULFILLED");

        // And its exception is raised anyway, because the issues panel is not the page's.
        firstPage.Exceptions.Should().Contain(e => e.Type == "UNFULFILLED" || e.Type == "UNPAID_FULFILLMENT",
            "the most urgent order is usually the oldest one, and a page-scoped list would hide it");

        var secondPage = await handler.HandleAsync(new GetReconciliationQuery(Page: 2, Limit: 1));
        var lastPage = await handler.HandleAsync(new GetReconciliationQuery(Page: 3, Limit: 1));

        var ids = firstPage.ThreeWayMatch.Items
            .Concat(secondPage.ThreeWayMatch.Items)
            .Concat(lastPage.ThreeWayMatch.Items)
            .Select(t => t.OrderId)
            .ToList();

        ids.Should().HaveCount(3, "the three pages hold the three orders");
        ids.Should().OnlyHaveUniqueItems("two pages never show the same order");

        lastPage.ThreeWayMatch.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task A_Caller_Asking_For_The_Whole_Table_Gets_A_Bounded_Page()
    {
        await using var context = CreateContext();
        await SeedAsync(context);

        var handler = new GetReconciliationQueryHandler(context);

        var result = await handler.HandleAsync(new GetReconciliationQuery(Page: 1, Limit: 100_000));

        result.ThreeWayMatch.PageSize.Should().Be(PageLimits.MaxPageSize,
            "a page size is clamped, so one request cannot ask for the whole table");
        result.ThreeWayMatch.Items.Should().HaveCount(3, "there are only that many rows to return");
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

        var fuelId = Guid.NewGuid();
        context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = fuelId,
            BrandId = brand.Id,
            Name = $"Reconciliation paging test fuel {Guid.NewGuid():N}",
            BrandFuelCode = $"recon-paging-{Guid.NewGuid():N}",
            BasePrice = 1200m,
            DiscountPrice = 1000m,
            MarginUahPerLiter = 10m,
            PumpPricePerLiter = 1200m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        context.FuelNominals.Add(new FuelNominal
        {
            FuelTypeId = fuelId,
            Liters = 50m,
            DataKind = DataKinds.Real,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var baseTime = new DateTime(2026, 4, 5, 9, 0, 0, DateTimeKind.Utc);

        // An old one the brand never fulfilled: the issue that has been waiting longest, and so
        // the one that would be on the last page of a list sorted newest first.
        AddOrder(context, userId, fuelId, brand.Slug, baseTime, paid: true, delivered: false);

        // And two ordinary ones, newer.
        AddOrder(context, userId, fuelId, brand.Slug, baseTime.AddMinutes(1), paid: true, delivered: true);
        AddOrder(context, userId, fuelId, brand.Slug, baseTime.AddMinutes(2), paid: true, delivered: true);

        await context.SaveChangesAsync();
    }

    private static void AddOrder(
        ApplicationDbContext context,
        Guid userId,
        Guid fuelId,
        string brandSlug,
        DateTime createdAt,
        bool paid,
        bool delivered)
    {
        var orderId = Guid.NewGuid();
        context.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 1000m,
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
                    Brand = brandSlug,
                    // The brand's key beside its name - the seeded network's own key, which is what
                    // the composite foreign key over (fuel type, brand) checks the pair against.
                    BrandId = BrandIds.ForSlug(brandSlug),
                    FuelTypeId = fuelId,
                    Liters = 50m,
                    Quantity = 1,
                    UnitPrice = 1000m,
                    LineTotal = 1000m
                }
            }
        });

        if (!delivered)
        {
            return;
        }

        var voucherId = Guid.NewGuid();
        context.FuelVouchers.Add(new FuelVoucher
        {
            Id = voucherId,
            OrderId = orderId,
            Brand = brandSlug,
            // The brand's key beside its name - the seeded network's own key.
            BrandId = BrandIds.ForSlug(brandSlug),
            FuelTypeId = fuelId,
            Liters = 50m,
            VoucherNumber = $"recon-page-{voucherId:N}"[..20],
            QrPayload = $"qr-{voucherId:N}",
            Status = VoucherStatus.Assigned,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt
        });

        context.Fulfillments.Add(new Fulfillment
        {
            OrderId = orderId,
            VoucherId = voucherId,
            FulfilledAtUtc = createdAt
        });
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
