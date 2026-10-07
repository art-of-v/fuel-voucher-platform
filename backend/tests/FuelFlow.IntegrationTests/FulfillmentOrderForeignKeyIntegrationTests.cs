using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>fulfillments.order_id</c> shipped as a bare column: no foreign key, only an index. That let an
/// order be deleted while its fulfillments survived, and since the wallet nests a customer's
/// vouchers by reading exactly these rows (<c>GetUserPurchases</c> takes voucher ids from
/// <c>fulfillments</c>, not from <c>fuel_vouchers.order_id</c>), the vouchers a customer was holding
/// went invisible in the app while still sitting in the table - with no error anywhere to explain it.
///
/// The constraint is added by migration rather than by the model, because EF scaffolds this
/// relationship onto a second shadow column instead of the existing <c>order_id</c>. So the behaviour
/// is pinned against real Postgres here: an InMemory provider enforces no foreign keys at all and
/// would pass regardless.
/// </summary>
[Collection("Integration Tests")]
public sealed class FulfillmentOrderForeignKeyIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FulfillmentOrderForeignKeyIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DeletingAnOrder_TakesItsFulfillmentsWithIt_AndLeavesTheVoucherAlone()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            seed.Users.Add(new User { Id = userId, PhoneNumber = $"+3809{DateTime.UtcNow:HHmmssff}" });
            seed.FuelVouchers.Add(AssignedVoucher(voucherId));
            seed.Orders.Add(Order(orderId, userId));
            await seed.SaveChangesAsync();

            seed.Fulfillments.Add(new Fulfillment
            {
                OrderId = orderId,
                VoucherId = voucherId,
                FulfilledAtUtc = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            var order = await ctx.Orders.SingleAsync(o => o.Id == orderId);
            ctx.Orders.Remove(order);
            await ctx.SaveChangesAsync();
        }

        using var verify = CreateContext();
        // The order owned the fulfillment, so it cannot outlive it.
        (await verify.Fulfillments.AsNoTracking().CountAsync(f => f.OrderId == orderId))
            .Should().Be(0);
        // ...but the fuel itself is untouched: deleting an order is not a way to lose a voucher's
        // record, and fuel_vouchers.order_id restricts the other direction for exactly that reason.
        (await verify.FuelVouchers.AsNoTracking().AnyAsync(v => v.Id == voucherId)).Should().BeTrue();
    }

    [Fact]
    public async Task TheConstraint_IsEnforcedByTheDatabase_NotOnlyByTheApplication()
    {
        using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        // Proved through the catalog rather than by triggering the error, so the assertion does not
        // depend on Postgres error text. A fulfillment pointing at a missing order is exactly what
        // the constraint forbids; without it the insert succeeds and the row is invisible forever.
        var constraints = await ctx.Database
            .SqlQueryRaw<string>(
                """
                SELECT conname AS "Value"
                FROM pg_constraint
                WHERE conrelid = 'fulfillments'::regclass
                  AND contype = 'f'
                  AND conname = 'FK_fulfillments_orders_order_id'
                """)
            .ToListAsync();

        constraints.Should().ContainSingle();
    }

    private static FuelVoucher AssignedVoucher(Guid id)
        => new()
        {
            Id = id,
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 10m,
            ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            VoucherNumber = $"fk-test-{id:N}",
            QrPayload = $"fk-qr-{id:N}",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static Order Order(Guid orderId, Guid userId)
        => new()
        {
            Id = orderId,
            UserId = userId,
            Price = 500m,
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };

    private static async Task ResetDataAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE "refunds", "fulfillments", "voucher_renewal_items", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE
            """);
    }

    private ApplicationDbContext CreateContext()
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(_fixture.DbContainer.GetConnectionString())
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .Options
        );
}