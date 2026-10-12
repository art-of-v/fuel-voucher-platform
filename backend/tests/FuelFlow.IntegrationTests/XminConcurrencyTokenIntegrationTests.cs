using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.Renewal.Operator;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Covers #246's detector: the <c>xmin</c> concurrency token on the four tables that are written
/// exclusively through the change tracker.
/// <para>
/// Two things have to hold, and both are asserted here rather than assumed. The migration has to be
/// applicable to a fresh database - this fixture runs <c>MigrateAsync()</c>, so the mere fact that
/// these tests execute proves the hand-edited no-op migration is valid, where EF's scaffolded
/// <c>ADD COLUMN "xmin"</c> would have thrown "conflicts with a system column name" and taken every
/// startup down with it. And the token has to actually detect a lost update, which is the entire
/// reason it exists.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class XminConcurrencyTokenIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public XminConcurrencyTokenIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_Normal_Refund_Save_Still_Works_With_The_Token_On()
    {
        var orderId = Guid.NewGuid();
        var refund = await SeedRefundAsync(orderId, amount: 1000);

        // The token adds `AND xmin = @original` to the UPDATE. A save that nobody else raced must
        // still land - a token that breaks ordinary writes is worse than no token.
        refund.ErrorMessage = "written once";
        await using (var context = CreateContext())
        {
            var loaded = await context.Refunds.FirstAsync(r => r.Id == refund.Id);
            loaded.ErrorMessage = "written once";
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        (await verify.Refunds.Where(r => r.Id == refund.Id).Select(r => r.ErrorMessage).SingleAsync())
            .Should().Be("written once");
    }

    /// <summary>
    /// The detector doing its job. Context A reads the row; context B changes it; context A then
    /// writes. Without the token, A's write silently overwrites B's and nobody ever finds out. With
    /// it, A gets <see cref="DbUpdateConcurrencyException"/> - and ApplicationDbContext logs the
    /// entity and key on the way past, which is the evidence #246's next stage needs.
    /// </summary>
    [Fact]
    public async Task A_Write_Against_A_Row_That_Changed_Is_Refused_Rather_Than_Overwriting()
    {
        var orderId = Guid.NewGuid();
        var refund = await SeedRefundAsync(orderId, amount: 1000);

        // A holds a stale copy, exactly as a handler that read the row before doing something slow
        // would. TrackAll is the app-wide default, so a plain query attaches.
        await using var stale = CreateContext();
        var snapshot = await stale.Refunds.FirstAsync(r => r.Id == refund.Id);
        snapshot.Amount = 2000;

        // Meanwhile somebody else moves the same row.
        await using (var other = CreateContext())
        {
            var affected = await other.Refunds
                .Where(r => r.Id == refund.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Amount, 5000));
            affected.Should().Be(1, "precondition: the other writer moved the row");
        }

        var threw = false;
        try
        {
            await stale.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            threw = true;
        }

        threw.Should().BeTrue("the token must refuse the write, not silently let it overwrite");

        await using var verify = CreateContext();
        var stored = await verify.Refunds
            .Where(r => r.Id == refund.Id)
            .Select(r => new { r.Amount, r.ErrorMessage })
            .SingleAsync();

        stored.Amount.Should().Be(5000, "the winning write stands - the loser's 2000 never landed");
        stored.ErrorMessage.Should().BeNull("and the loser's other change went with it");
    }

    /// <summary>
    /// Pins the table set. <c>fuel_vouchers</c> and <c>orders</c> are written with raw SQL as well as
    /// through the tracker; a raw UPDATE bumps <c>xmin</c> behind EF's back and the tracked save then
    /// fails a predicate that never actually raced. Adding the token to those two turns a working
    /// handler into a 409, which is why they are deliberately not in this list.
    /// </summary>
    [Fact]
    public async Task The_Token_Is_On_The_Four_Tracker_Only_Tables_And_Not_On_The_Raw_Sql_Ones()
    {
        await using var context = CreateContext();

        var tokenised = context.Model.GetEntityTypes()
            .Where(t => t.FindProperty(PostgresConcurrencyTokenProbe.PropertyName) is { IsConcurrencyToken: true })
            .Select(t => t.GetTableName())
            .OrderBy(n => n)
            .ToList();

        tokenised.Should().BeEquivalentTo(
            ["operator_voucher_renewals", "refunds", "voucher_exchanges", "voucher_renewal_items"]);

        foreach (var rawSqlTable in new[] { "fuel_vouchers", "orders" })
        {
            var entity = context.Model.GetEntityTypes().FirstOrDefault(t => t.GetTableName() == rawSqlTable);
            entity.Should().NotBeNull($"{rawSqlTable} is mapped");
            var property = entity!.FindProperty(PostgresConcurrencyTokenProbe.PropertyName);
            property.Should().BeNull($"{rawSqlTable} is written with raw SQL, so xmin there would fail saves that did not race");
        }
    }

    private async Task<Refund> SeedRefundAsync(Guid orderId, int amount)
    {
        await using var seed = CreateContext();
        await ResetDataAsync(seed);

        seed.Users.Add(new FuelFlow.SharedKernel.Domain.User
        {
            Id = orderId,
            PhoneNumber = $"+38{orderId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        seed.Orders.Add(new Order
        {
            Id = orderId,
            UserId = orderId,
            Price = 100m,
            Status = OrderStatus.Refunded,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var refund = new Refund
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            UserId = orderId,
            Amount = amount,
            InvoiceId = $"inv-{orderId:N}"[..Math.Min(100, 4 + 32)],
            ExtRef = $"ref-{orderId:N}"[..Math.Min(150, 4 + 32)],
            Status = RefundStatus.Processing,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        seed.Refunds.Add(refund);

        await seed.SaveChangesAsync();
        return refund;
    }

    private static async Task ResetDataAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "refunds", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll)
            .Options);
}

/// <summary>
/// The shadow property name lives in an internal helper, so the test project cannot see it. Duplicated
/// as a literal on purpose: if the helper is renamed and this is not, the test above fails loudly
/// rather than silently asserting nothing.
/// </summary>
internal static class PostgresConcurrencyTokenProbe
{
    internal const string PropertyName = "xmin";
}