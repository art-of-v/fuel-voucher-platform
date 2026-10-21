using FluentAssertions;
using FuelFlow.Features.Auth.AdminUser.GetAdminUserOptions;
using FuelFlow.Features.Auth.AdminUser.GetAdminUsers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The admin customers table is a page, not the whole table.
/// </summary>
/// <remarks>
/// <para>
/// <c>GET /api/admin/users</c> answered with every customer the platform has ever registered.
/// The list grew with every registration, the screen got slower as the business did, and the server
/// held every customer in memory to draw a screen that shows fifty rows.
/// </para>
/// <para>
/// The filters were already the database's job - a search for one customer no longer read the whole
/// table - so what was left was the materialisation: an operator opening the customers tab with no
/// search asked for every customer there was.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class AdminUsersPagingIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private const int UserCount = 7;

    private readonly TestDatabaseFixture _fixture;

    public AdminUsersPagingIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_Customers_List_Answers_With_One_Page_And_The_Count_Behind_It()
    {
        await using var context = CreateContext();
        await SeedAsync(context);

        var handler = new GetAdminUsersQueryHandler(context);

        var firstPage = await handler.HandleAsync(new GetAdminUsersQuery(Page: 1, Limit: 3));

        firstPage.TotalCount.Should().Be(UserCount, "the count says how many there are, not how many came back");
        firstPage.Items.Should().HaveCount(3, "the page holds what was asked for");
        firstPage.Page.Should().Be(1);
        firstPage.PageSize.Should().Be(3);
        firstPage.TotalPages.Should().Be(3);
        firstPage.HasPreviousPage.Should().BeFalse();
        firstPage.HasNextPage.Should().BeTrue();

        var lastPage = await handler.HandleAsync(new GetAdminUsersQuery(Page: 3, Limit: 3));

        lastPage.Items.Should().HaveCount(1, "the last page holds the remainder");
        lastPage.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task Two_Pages_Never_Show_The_Same_Customer()
    {
        await using var context = CreateContext();
        await SeedAsync(context);

        var handler = new GetAdminUsersQueryHandler(context);

        var first = await handler.HandleAsync(new GetAdminUsersQuery(Page: 1, Limit: 3));
        var second = await handler.HandleAsync(new GetAdminUsersQuery(Page: 2, Limit: 3));

        var firstIds = first.Items.Select(u => u.Id).ToList();
        var secondIds = second.Items.Select(u => u.Id).ToList();

        firstIds.Intersect(secondIds).Should().BeEmpty(
            "the order needs a tie-break, or two customers registered in the same tick swap places between pages");
    }

    [Fact]
    public async Task A_Caller_Asking_For_Every_Customer_Gets_A_Bounded_Page()
    {
        await using var context = CreateContext();
        await SeedAsync(context);

        var handler = new GetAdminUsersQueryHandler(context);

        var result = await handler.HandleAsync(new GetAdminUsersQuery(Page: 1, Limit: 100_000));

        result.PageSize.Should().Be(PageLimits.MaxPageSize,
            "a page size is clamped, so one request cannot ask for every customer the platform has");
        result.Items.Should().HaveCount(UserCount, "there are only that many rows to return");
    }

    [Fact]
    public async Task A_Search_Narrows_The_Count_As_Well_As_The_Page()
    {
        await using var context = CreateContext();
        await SeedAsync(context);

        var handler = new GetAdminUsersQueryHandler(context);

        var result = await handler.HandleAsync(new GetAdminUsersQuery(Search: "customer-3"));

        result.TotalCount.Should().Be(1, "the count answers the same question the page does");
        result.Items.Should().ContainSingle();
        result.Items[0].FirstName.Should().Be("customer-3");
    }

    [Fact]
    public async Task The_Options_List_Names_Every_Customer_And_Nothing_Else()
    {
        await using var context = CreateContext();
        await SeedAsync(context);

        var handler = new GetAdminUserOptionsQueryHandler(context);

        var options = await handler.HandleAsync();

        options.Should().HaveCount(UserCount,
            "the screens that name a customer need every customer, not the page the admin is on");
        options.Should().OnlyContain(o => o.Id != null && o.Phone != null);
        options.Should().Contain(o => o.FirstName == "customer-3");
    }

    private async Task SeedAsync(ApplicationDbContext context)
    {
        await context.Database.MigrateAsync();

        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

        for (var i = 0; i < UserCount; i++)
        {
            context.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = $"+38067000{i:D4}",
                FirstName = $"customer-{i}",
                IsActive = true,
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                UpdatedAtUtc = TestClock.UtcNow
            });
        }

        await context.SaveChangesAsync();
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
