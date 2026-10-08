using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Orders.DeleteOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.Renewal.Operator;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers.UpdateVoucher;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// A voucher in someone's hands must always be traceable to the order that put it there
/// (<c>fuel_vouchers.order_id</c>). The claim that hands fuel over is a raw SQL UPDATE, so these
/// tests drive the real <see cref="FulfillmentService"/> against real Postgres: an InMemory
/// provider would happily accept an assignment that forgets the order, which is exactly the bug
/// this column exists to make impossible.
/// </summary>
[Collection("Integration Tests")]
public sealed class VoucherOrderOwnershipIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public VoucherOrderOwnershipIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessPendingOrdersAsync_StampsTheOwningOrderOnEveryDeliveredVoucher()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            seed.FuelVouchers.Add(StockVoucher(Guid.NewGuid(), "OKKO", "okko-95", 50m));
            seed.Orders.Add(PurchaseOrder(orderId, userId, ("OKKO", "okko-95", 50m, 2500)));

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx).ProcessPendingOrdersAsync();
        }

        using var verify = CreateContext();
        var delivered = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Status == VoucherStatus.Assigned);

        delivered.OrderId.Should().Be(orderId);

        var fulfillments = await verify.Fulfillments.AsNoTracking().Where(f => f.OrderId == orderId).ToListAsync();
        fulfillments.Should().ContainSingle().Which.VoucherId.Should().Be(delivered.Id);
    }

    [Fact]
    public async Task ProcessPendingOrdersAsync_LeavesUndeliveredStockWithoutAnOrder()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            // Two vouchers ordered, three in stock: the third one is the spare.
            seed.FuelVouchers.Add(StockVoucher(Guid.NewGuid(), "OKKO", "okko-95", 50m));
            seed.FuelVouchers.Add(StockVoucher(Guid.NewGuid(), "OKKO", "okko-95", 50m));
            seed.FuelVouchers.Add(StockVoucher(Guid.NewGuid(), "OKKO", "okko-95", 50m));
            seed.Orders.Add(PurchaseOrder(
                orderId,
                userId,
                ("OKKO", "okko-95", 50m, 2500),
                ("OKKO", "okko-95", 50m, 2500)));

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx).ProcessPendingOrdersAsync();
        }

        using var verify = CreateContext();

        var assigned = await verify.FuelVouchers.AsNoTracking()
            .Where(v => v.Status == VoucherStatus.Assigned)
            .ToListAsync();

        assigned.Should().HaveCount(2);
        assigned.Should().OnlyContain(v => v.OrderId == orderId);

        // The spare was never handed to anybody, so it must not be credited to this order: an
        // order_id here would be a delivery that never happened.
        var leftover = await verify.FuelVouchers.AsNoTracking()
            .Where(v => v.Status == VoucherStatus.Available)
            .ToListAsync();

        leftover.Should().ContainSingle().Which.OrderId.Should().BeNull();
    }

    [Fact]
    public async Task ProcessRenewalOrderAsync_ReplacementBelongsToTheRenewalOrderAndSourceKeepsItsPurchase()
    {
        var userId = Guid.NewGuid();
        var purchaseId = Guid.NewGuid();
        var renewalId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);

            // The lapsed voucher the customer is holding, bought under an earlier purchase.
            seed.Orders.Add(PurchaseOrder(purchaseId, userId, ("WOG", "wog-95", 40m, 2400)));
            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, "WOG", "wog-95", 40m, today.AddDays(-3), purchaseId));
            seed.FuelVouchers.Add(StockVoucher(Guid.NewGuid(), "WOG", "wog-95", 40m, today.AddMonths(1)));

            seed.Orders.Add(RenewalOrder(renewalId, userId, 480));
            seed.VoucherRenewalItems.Add(new VoucherRenewalItem
            {
                Id = itemId,
                OrderId = renewalId,
                SourceVoucherId = sourceId,
                TermCode = "1m",
                CreatedAtUtc = DateTime.UtcNow
            });

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx).ProcessRenewalOrderAsync(renewalId);
        }

        using var verify = CreateContext();

        var replacementId = await verify.VoucherRenewalItems.AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => i.FulfilledVoucherId)
            .SingleAsync();
        replacementId.Should().NotBeNull();

        var replacement = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == replacementId);
        replacement.Status.Should().Be(VoucherStatus.Assigned);
        replacement.OrderId.Should().Be(renewalId);

        // The replaced voucher is stock again, but it still belongs to the purchase that delivered it;
        // re-homing it to the renewal would rewrite where the fuel originally came from.
        var source = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == sourceId);
        source.Status.Should().Be(VoucherStatus.Available,
            "a replaced voucher goes back into the sellable pool — the next customer takes it and pays");
        source.OrderId.Should().Be(purchaseId);
    }

    [Fact]
    public async Task DeleteOrder_RefusesWhileVouchersAreStillRecordedAgainstIt()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            seed.Orders.Add(PurchaseOrder(orderId, userId, ("OKKO", "okko-95", 50m, 2500)));
            seed.FuelVouchers.Add(StockVoucher(voucherId, "OKKO", "okko-95", 50m));
            await seed.SaveChangesAsync();

            await seed.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "fuel_vouchers" SET status = 'Assigned', assigned_to_user_id = {userId}, order_id = {orderId} WHERE id = {voucherId}""");
        }

        using var ctx = CreateContext();
        var handler = new DeleteOrderCommandHandler(ctx);

        var refused = async () => await handler.HandleAsync(new DeleteOrderCommand(orderId));

        // ArgumentException is this codebase's validation channel and surfaces as 400 with the
        // message; a bare FK violation would have surfaced as an opaque 400 or a 500.
        await refused.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*cannot be deleted*");

        (await ctx.Orders.AsNoTracking().AnyAsync(o => o.Id == orderId)).Should().BeTrue();

        // Once the voucher is back in the warehouse it is no longer recorded against the order, and the
        // order goes away normally. Back to stock is how that actually happens.
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "fuel_vouchers" SET status = 'Available', assigned_to_user_id = NULL, order_id = NULL WHERE id = {voucherId}""");

        (await handler.HandleAsync(new DeleteOrderCommand(orderId))).Should().BeTrue();
    }

    [Fact]
    public async Task Database_RefusesToPutWarehouseStockIntoSomeonesHandsWithoutAnOrder()
    {
        var userId = Guid.NewGuid();
        var stockId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 50m));
            await seed.SaveChangesAsync();
        }

        using var ctx = CreateContext();

        // The rule, enforced by the table itself rather than by any handler remembering to check:
        // ck_voucher_held_has_order. 23514 is Postgres's check_violation.
        var heldWithoutOrder = async () => await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "fuel_vouchers" SET status = 'Assigned', assigned_to_user_id = {userId} WHERE id = {stockId}""");

        var violation = await heldWithoutOrder.Should().ThrowAsync<PostgresException>();
        violation.Which.SqlState.Should().Be("23514");

        // Same row as stock is fine - stock belongs to no order until it is handed over.
        (await ctx.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == stockId)).Status
            .Should().Be(VoucherStatus.Available);
    }

    [Fact]
    public async Task AdminVoucherEditor_RefusesToHandWarehouseStockToSomebodyWithNoOrder()
    {
        var stockId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 50m));
            await seed.SaveChangesAsync();
        }

        using var ctx = CreateContext();
        var handler = new UpdateVoucherCommandHandler(ctx, new ProviderEventService(ctx));

        foreach (var status in new[] { VoucherStatus.Assigned, VoucherStatus.Used, VoucherStatus.Blocked })
        {
            var result = await handler.HandleAsync(
                new UpdateVoucherCommand(stockId, status.ToString(), null, null, null));

            result.Should().NotBeNull();
            result!.Success.Should().BeFalse(status.ToString());
            result.Error.Should().Contain("without an order");
        }

        (await ctx.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == stockId)).Status
            .Should().Be(VoucherStatus.Available);
    }

    [Fact]
    public async Task OperatorRenewal_CreatesARenewalOrderCarryingTheSurchargeAndOwnsTheReplacement()
    {
        var userId = Guid.NewGuid();
        var purchaseId = Guid.NewGuid();
        var customerVoucherId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        const decimal surcharge = 250m;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);

            seed.Orders.Add(PurchaseOrder(purchaseId, userId, ("OKKO", "okko-95", 50m, 2500)));
            // The customer's voucher has lapsed, so the operator takes a fresh one out of the warehouse.
            seed.FuelVouchers.Add(SourceVoucher(customerVoucherId, userId, "OKKO", "okko-95", 50m, today.AddDays(-2), purchaseId));
            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 50m));

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            var handler = new ConfirmOperatorRenewalCommandHandler(ctx, new ProviderEventService(ctx));

            var result = await handler.HandleAsync(new ConfirmOperatorRenewalCommand
            {
                VoucherId = customerVoucherId,
                TermCode = "1m",
                SurchargeUah = surcharge,
                ActingUserId = Guid.NewGuid(),
                ActingUserName = "operator"
            });

            result.ReplacementVoucherId.Should().Be(stockId);
        }

        using var verify = CreateContext();

        // The surcharge is money the customer actually paid the operator, so it gets an order - the
        // same shape the self-service renewal already produces - instead of leaving the replacement
        // voucher in the customer's hands with nothing behind it.
        var renewalOrder = await verify.Orders.AsNoTracking()
            .SingleAsync(o => o.Id != purchaseId);

        renewalOrder.Kind.Should().Be(OrderKind.Renewal);
        renewalOrder.Price.Should().Be(surcharge);
        renewalOrder.UserId.Should().Be(userId);
        renewalOrder.Status.Should().Be(OrderStatus.Fulfilled);

        (await verify.OrderLineItems.AsNoTracking().Where(l => l.OrderId == renewalOrder.Id).ToListAsync())
            .Should().ContainSingle();

        (await verify.Fulfillments.AsNoTracking().Where(f => f.OrderId == renewalOrder.Id).ToListAsync())
            .Should().ContainSingle().Which.VoucherId.Should().Be(stockId);

        var replacement = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == stockId);
        replacement.Status.Should().Be(VoucherStatus.Assigned);
        replacement.OrderId.Should().Be(renewalOrder.Id);

        // The lapsed voucher keeps the purchase it arrived under - it was not re-homed.
        (await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == customerVoucherId)).OrderId
            .Should().Be(purchaseId);
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static FulfillmentService BuildService(ApplicationDbContext ctx)
        => new(
            ctx,
            NullLogger<FulfillmentService>.Instance,
            new RefundOrderCommandHandler(ctx, new Mock<IMonobankClient>().Object, new ProviderEventService(ctx)),
            new RuntimeSettingsService(ctx),
            NotificationDispatcher.Disabled,
            new ConfigurationBuilder().Build());

    private static void SeedUser(ApplicationDbContext ctx, Guid userId)
        => ctx.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

    private static FuelVoucher StockVoucher(Guid id, string provider, string fuelTypeId, decimal liters, DateOnly? expiry = null)
        => new()
        {
            Id = id,
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            ProviderExpirationDate = expiry ?? DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            CustomerExpirationDate = expiry ?? DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            VoucherNumber = $"STK-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static FuelVoucher SourceVoucher(Guid id, Guid userId, string provider, string fuelTypeId, decimal liters, DateOnly expiry, Guid? orderId = null)
        => new()
        {
            Id = id,
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            ProviderExpirationDate = expiry,
            CustomerExpirationDate = expiry,
            VoucherNumber = $"SRC-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            OrderId = orderId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static Order PurchaseOrder(Guid orderId, Guid userId, params (string Provider, string FuelTypeId, decimal Liters, decimal Amount)[] lines)
    {
        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = lines.Sum(l => l.Amount),
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };

        foreach (var (provider, fuelTypeId, liters, amount) in lines)
        {
            order.LineItems.Add(new OrderLineItem
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Provider = provider,
                FuelTypeId = fuelTypeId,
                Liters = liters,
                Quantity = 1,
                UnitPrice = amount,
                LineTotal = amount
            });
        }

        return order;
    }

    private static Order RenewalOrder(Guid orderId, Guid userId, decimal price)
        => new()
        {
            Id = orderId,
            UserId = userId,
            Price = price,
            Kind = OrderKind.Renewal,
            Status = OrderStatus.PendingFulfillment,
            MonobankInvoiceId = $"test-invoice-{orderId:N}",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };

    private static async Task ResetDataAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "refunds", "fulfillments", "voucher_renewal_items", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}