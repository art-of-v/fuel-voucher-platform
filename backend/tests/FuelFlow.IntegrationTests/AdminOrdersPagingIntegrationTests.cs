using FluentAssertions;
using FuelFlow.Features.Orders.GetAdminOrders;
using FuelFlow.Features.Orders.GetAdminPurchases;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The admin order lists are pages, not the whole table.
/// </summary>
/// <remarks>
/// <para>
/// Both handlers used to answer with every order the shop had ever taken. The list grew with
/// the business, the screen got slower as the business did, and the server held every order
/// in memory to draw a screen that shows fifty rows.
/// </para>
/// <para>
/// <b>Why this is a database test and not a unit test.</b> The page is taken by a SQL
/// <c>OFFSET</c> over an ordered query, and the order needs a tie-break: two orders created in
/// the same tick would otherwise swap places between two pages, and one of them would be
/// shown twice. The in-memory provider does not order the way Postgres does, so only a real
/// database can say whether the page holds.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class AdminOrdersPagingIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private const int OrderCount = 7;

    private readonly TestDatabaseFixture _fixture;

    public AdminOrdersPagingIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_Orders_List_Answers_With_One_Page_And_The_Count_Behind_It()
    {
        await using var context = CreateContext();
        await SeedAsync(context);

        var handler = new GetAdminOrdersQueryHandler(context);

        var firstPage = await handler.HandleAsync(new GetAdminOrdersQuery(Page: 1, Limit: 3));

        firstPage.TotalCount.Should().Be(OrderCount, "the count says how many there are, not how many came back");
        firstPage.Items.Should().HaveCount(3, "the page holds what was asked for");
        firstPage.Page.Should().Be(1);
        firstPage.PageSize.Should().Be(3);
        firstPage.TotalPages.Should().Be(3);
        firstPage.HasPreviousPage.Should().BeFalse();
        firstPage.HasNextPage.Should().BeTrue();

        var secondPage = await handler.HandleAsync(new GetAdminOrdersQuery(Page: 2, Limit: 3));

        secondPage.Items.Should().HaveCount(3);
        secondPage.HasPreviousPage.Should().BeTrue();

        var lastPage = await handler.HandleAsync(new GetAdminOrdersQuery(Page: 3, Limit: 3));

        lastPage.Items.Should().HaveCount(1, "the last page holds the remainder");
        lastPage.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task Two_Pages_Never_Show_The_Same_Order()
    {
        await using var context = CreateContext();
        await SeedAsync(context);

        var handler = new GetAdminOrdersQueryHandler(context);

        var first = await handler.HandleAsync(new GetAdminOrdersQuery(Page: 1, Limit: 3));
        var second = await handler.HandleAsync(new GetAdminOrdersQuery(Page: 2, Limit: 3));

        var firstIds = first.Items.Select(o => o.Id).ToList();
        var secondIds = second.Items.Select(o => o.Id).ToList();

        firstIds.Intersect(secondIds).Should().BeEmpty(
            "the order needs a tie-break, or two orders created in the same tick swap places between pages");
        (firstIds.Count + secondIds.Count).Should().Be(6);
    }

    [Fact]
    public async Task A_Caller_Asking_For_The_Whole_Table_Gets_A_Bounded_Page()
    {
        await using var context = CreateContext();
        await SeedAsync(context);

        var handler = new GetAdminOrdersQueryHandler(context);

        var result = await handler.HandleAsync(new GetAdminOrdersQuery(Page: 1, Limit: 100_000));

        result.PageSize.Should().Be(PageLimits.MaxPageSize,
            "a page size is clamped, so one request cannot ask for the whole table");
        result.Items.Should().HaveCount(OrderCount, "there are only that many rows to return");
    }

    [Fact]
    public async Task The_Purchases_List_Answers_With_One_Page_And_The_Count_Behind_It()
    {
        await using var context = CreateContext();
        await SeedAsync(context);

        var handler = new GetAdminPurchasesQueryHandler(context);

        var firstPage = await handler.HandleAsync(new GetAdminPurchasesQuery(Page: 1, Limit: 3));

        firstPage.TotalCount.Should().Be(OrderCount);
        firstPage.Items.Should().HaveCount(3);
        firstPage.TotalPages.Should().Be(3);

        var lastPage = await handler.HandleAsync(new GetAdminPurchasesQuery(Page: 3, Limit: 3));

        lastPage.Items.Should().HaveCount(1);
        lastPage.HasNextPage.Should().BeFalse();
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

        // Distinct timestamps, so the ORDER BY has something to sort on.
        for (var i = 0; i < OrderCount; i++)
        {
            var orderId = Guid.NewGuid();
            var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i);

            context.Orders.Add(new Order
            {
                Id = orderId,
                UserId = userId,
                Price = 2500,
                Status = OrderStatus.Fulfilled,
                CreatedAtUtc = createdAt,
                UpdatedAtUtc = createdAt,
                FulfilledAtUtc = createdAt,
                LineItems = new List<OrderLineItem>
                {
                    new OrderLineItem
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderId,
                        BrandId = BrandIds.ForSlug("okko"),
                        Brand = "okko",
                        FuelTypeId = ToUuid("okko-95"),
                        Liters = 50m,
                        Quantity = 1,
                        UnitPrice = 2500,
                        LineTotal = 2500
                    }
                }
            });
        }

        await context.SaveChangesAsync();
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
