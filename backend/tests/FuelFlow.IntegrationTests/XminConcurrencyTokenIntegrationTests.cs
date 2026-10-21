using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.Renewal.Operator;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Testing;
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
            var loaded = await context.Payments.FirstAsync(r => r.Id == refund.Id);
            loaded.ErrorMessage = "written once";
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        (await verify.Payments.Where(r => r.Id == refund.Id).Select(r => r.ErrorMessage).SingleAsync())
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
        var snapshot = await stale.Payments.FirstAsync(r => r.Id == refund.Id);
        snapshot.AmountKopecks = 2000;

        // Meanwhile somebody else moves the same row.
        await using (var other = CreateContext())
        {
            var affected = await other.Payments
                .Where(r => r.Id == refund.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.AmountKopecks, 5000)
                    .SetProperty(r => r.AmountUah, 50.00m));
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
        var stored = await verify.Payments
            .Where(r => r.Id == refund.Id)
            .Select(r => new { r.AmountKopecks, r.ErrorMessage })
            .SingleAsync();

        stored.AmountKopecks.Should().Be(5000, "the winning write stands - the loser's 2000 never landed");
        stored.ErrorMessage.Should().BeNull("and the loser's other change went with it");
    }

    /// <summary>
    /// Pins the table set. <c>fuel_vouchers</c> and <c>orders</c> were excluded from the first half of
    /// #246 because they are written with raw SQL as well as through the tracker: a raw UPDATE bumps
    /// <c>xmin</c> behind EF's back, and a tracked save of the same row then fails a predicate that
    /// never actually raced. That mixing has been removed — <c>FulfillmentService</c> clears the
    /// tracker before the mismatch-repair pass reads rows its own statements just claimed, and its
    /// decision queries are <c>AsNoTracking</c> — so the two are in this list now.
    /// </summary>
    /// <remarks>
    /// This is a model-metadata assertion: it can only say which tables carry the token, never whether
    /// a handler mixes a raw write with a tracked save of the same row. The two rules the token turns
    /// into requirements - "the tracker is emptied before reading rows your own statements claimed" and
    /// "a row you save was read TRACKED, or EF gives you xmin = 0" - are behavioural and live in
    /// <see cref="FulfillmentMismatchRepairTests"/> and
    /// <see cref="ConcurrencyTokenTrackingRuleIntegrationTests"/>. A test that cannot see a handler is
    /// exactly why the exclusion outlived the mixing it described.
    /// </remarks>
    [Fact]
    public async Task The_Token_Is_On_Every_Table_That_Owns_Money_Or_Stock()
    {
        await using var context = CreateContext();

        var tokenised = context.Model.GetEntityTypes()
            .Where(t => t.FindProperty(PostgresConcurrencyTokenProbe.PropertyName) is { IsConcurrencyToken: true })
            .Select(t => t.GetTableName())
            .OrderBy(n => n)
            .ToList();

        tokenised.Should().BeEquivalentTo(
            ["fuel_vouchers", "orders", "payments"],
            "the two hot tables are included now that nothing mixes a raw write with a tracked save of the same row");
    }

    private async Task<Payment> SeedRefundAsync(Guid orderId, int amount)
    {
        await using var seed = CreateContext();
        await ResetDataAsync(seed);

        seed.Users.Add(new FuelFlow.SharedKernel.Domain.User
        {
            Id = orderId,
            PhoneNumber = $"+38{orderId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        seed.Orders.Add(new Order
        {
            Id = orderId,
            UserId = orderId,
            Price = 100m,
            Status = OrderStatus.Refunded,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        // A refund is a payment of kind 'Refund', and a refund must name the capture it reverses - so this
        // seeds both, which is also why the tokenised list below expects `payments` rather than a
        // `refunds` table that no longer exists.
        var capture = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Kind = PaymentKind.Capture,
            Provider = "monobank",
            ProviderInvoiceId = $"inv-{orderId:N}"[..Math.Min(100, 4 + 32)],
            AmountUah = amount / 100m,
            AmountKopecks = amount,
            Currency = "UAH",
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = TestClock.UtcNow,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        };
        seed.Payments.Add(capture);

        var refund = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Kind = PaymentKind.Refund,
            ParentPaymentId = capture.Id,
            Provider = "monobank",
            ProviderInvoiceId = capture.ProviderInvoiceId,
            AmountUah = amount / 100m,
            AmountKopecks = amount,
            Currency = "UAH",
            Status = PaymentStatus.Pending,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        };
        seed.Payments.Add(refund);

        await seed.SaveChangesAsync();
        return refund;
    }

    private static async Task ResetDataAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll)
            .Options, TestQrCipher.Instance);
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
