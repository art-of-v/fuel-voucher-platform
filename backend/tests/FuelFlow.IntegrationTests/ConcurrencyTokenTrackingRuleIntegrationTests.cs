using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The rule that #246's token on <c>orders</c> and <c>fuel_vouchers</c> turns into a hard requirement,
/// stated as two facts about EF rather than as a comment nobody reads.
/// </summary>
/// <remarks>
/// <b>EF materializes a store-generated concurrency token only on a tracked load.</b> The same row read
/// with <c>AsNoTracking()</c> comes back with <c>xmin = 0</c>, so the <c>UPDATE</c> that
/// <c>DbSet.Update()</c> emits carries <c>WHERE id = @id AND xmin = 0</c> — matches nothing, and throws
/// <see cref="DbUpdateConcurrencyException"/> every single time. Not under a race. As a certainty.
///
/// This is the trap that made the mismatch-repair pass in <c>FulfillmentService</c> unfixable with a
/// concurrency token until its re-read was changed to a tracked one, and the reason the handler
/// comments now say "loaded TRACKED, and that is load-bearing".
/// </remarks>
[Collection("Integration Tests")]
public sealed class ConcurrencyTokenTrackingRuleIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ConcurrencyTokenTrackingRuleIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_Tracked_Read_Carries_Its_Token_And_Can_Be_Saved()
    {
        await using var context = CreateContext();
        var orderId = await SeedAsync();

        var order = await context.Orders.FirstAsync(o => o.Id == orderId);
        context.Entry(order).Property("xmin").CurrentValue.Should().NotBe(0u,
            "a tracked materialization is where the token comes from");

        order.Status = OrderStatus.PartiallyFulfilled;
        await context.SaveChangesAsync();

        var stored = await context.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        stored.Status.Should().Be(OrderStatus.PartiallyFulfilled);
    }

    [Fact]
    public async Task An_Untracked_Read_Saved_Through_Update_Always_Fails()
    {
        await using var context = CreateContext();
        var orderId = await SeedAsync();

        var order = await context.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        context.Entry(order).Property("xmin").CurrentValue.Should().Be(0u,
            "this is the whole trap: the column is in the SELECT, but an untracked entity is not given the value");

        order.Status = OrderStatus.PartiallyFulfilled;
        context.Orders.Update(order);

        // A failure every time, not only when something else is writing. Which is what makes it safe
        // to notice in a test: it does not need a concurrent writer to reproduce.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
    }

    private async Task<Guid> SeedAsync()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using var seed = CreateContext();
        await seed.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

        seed.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        AddPaidOrder(seed, new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 2500,
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await seed.SaveChangesAsync();
        return orderId;
    }

    /// <summary>The production default, deliberately: no <c>UseQueryTrackingBehavior</c> override.</summary>
    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
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
            PaidAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
    }
}