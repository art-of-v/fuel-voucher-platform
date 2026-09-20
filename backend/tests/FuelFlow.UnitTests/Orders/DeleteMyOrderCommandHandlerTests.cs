using FluentAssertions;
using FuelFlow.Features.Orders.DeleteMyOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FuelFlow.UnitTests.Orders;

public sealed class DeleteMyOrderCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_ShouldSoftDeleteOwnUnpaidOrder()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();
        var order = await AddOrderAsync(context, userId, OrderStatus.PendingPayment);
        var handler = new DeleteMyOrderCommandHandler(context);

        var result = await handler.HandleAsync(new DeleteMyOrderCommand(order.Id, userId), CancellationToken.None);

        result.Should().BeTrue();
        // The query filter hides soft-deleted rows, so a filtered read returns nothing.
        (await context.Orders.AnyAsync(o => o.Id == order.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnFalseForOrderOwnedByAnotherUser()
    {
        await using var context = CreateContext();
        var owner = Guid.NewGuid();
        var order = await AddOrderAsync(context, owner, OrderStatus.PendingPayment);
        var handler = new DeleteMyOrderCommandHandler(context);

        // A different user must not be able to delete someone else's order.
        var result = await handler.HandleAsync(new DeleteMyOrderCommand(order.Id, Guid.NewGuid()), CancellationToken.None);

        result.Should().BeFalse();
        (await context.Orders.IgnoreQueryFilters().FirstAsync(o => o.Id == order.Id))
            .IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ShouldRefuseToDeletePaidOrder()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();
        // Only PendingPayment is deletable; a paid order has money/vouchers attached.
        var order = await AddOrderAsync(context, userId, OrderStatus.PendingFulfillment);
        var handler = new DeleteMyOrderCommandHandler(context);

        var result = await handler.HandleAsync(new DeleteMyOrderCommand(order.Id, userId), CancellationToken.None);

        result.Should().BeFalse();
        (await context.Orders.FirstAsync(o => o.Id == order.Id)).IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ShouldPersistSoftDelete_UnderProductionNoTrackingDefault()
    {
        // Regression: prod runs the DbContext with QueryTrackingBehavior.NoTracking
        // (DatabaseSetup). This is a soft delete — the handler loads the order and sets
        // IsDeleted = true — so it must AsTracking() the query or SaveChanges silently
        // persists nothing: the DELETE returns 200, the app drops the card, and the order
        // reappears on the next refresh because the row's IsDeleted never flipped.
        // Separate contexts over one shared store prove the flag reaches the database
        // (a tracking-on context would mask the bug by mutating the in-memory instance).
        var root = new InMemoryDatabaseRoot();
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        Guid orderId;

        await using (var seed = CreateNoTrackingContext(dbName, root))
        {
            var order = await AddOrderAsync(seed, userId, OrderStatus.PendingPayment);
            orderId = order.Id;
        }

        await using (var act = CreateNoTrackingContext(dbName, root))
        {
            var handler = new DeleteMyOrderCommandHandler(act);
            var result = await handler.HandleAsync(new DeleteMyOrderCommand(orderId, userId), CancellationToken.None);
            result.Should().BeTrue();
        }

        await using var verify = CreateNoTrackingContext(dbName, root);
        var persisted = await verify.Orders.IgnoreQueryFilters().FirstAsync(o => o.Id == orderId);
        persisted.IsDeleted.Should().BeTrue("a soft delete must reach the database, not just mutate an in-memory copy");
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ApplicationDbContext CreateNoTrackingContext(string dbName, InMemoryDatabaseRoot root) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName, root)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);

    private static async Task<Order> AddOrderAsync(ApplicationDbContext context, Guid userId, OrderStatus status)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Price = 2500,
            Status = status,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order;
    }
}
