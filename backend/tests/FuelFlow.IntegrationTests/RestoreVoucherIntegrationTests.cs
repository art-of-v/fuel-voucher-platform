using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.RestoreVoucher;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Covers #242. The restore path used to read the voucher, check its status, and then write it
/// back through the change tracker - no ownership check, no atomic transition, and it left
/// <c>UsedAtUtc</c> populated after undoing a redemption.
/// <para>
/// These live in the integration suite rather than the unit suite deliberately: the fix is an
/// <c>ExecuteUpdateAsync</c> whose WHERE clause carries the status predicate, and the EF Core
/// in-memory brand neither translates <c>ExecuteUpdate</c> nor models row locking.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class RestoreVoucherIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public RestoreVoucherIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The regression test for #242, and the reason the handler was rewritten rather than patched.
    /// <see cref="RestoreVoucherCommandHandler"/> set the status back to Assigned but never cleared
    /// <c>UsedAtUtc</c>, so a restored voucher carried the timestamp of a redemption that no longer
    /// happened - which a worker usage report reads as "when was this redeemed".
    /// </summary>
    [Fact]
    public async Task RestoreVoucher_TransitionsFromUsedToAssigned_AndClearsTheRedemptionTime()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedUsedVoucherAsync(userId, voucherId);

        await using var context = CreateContext();
        var handler = new RestoreVoucherCommandHandler(context);

        var response = await handler.HandleAsync(new RestoreVoucherCommand(voucherId, userId));

        response.Success.Should().BeTrue();
        response.Message.Should().Be("Voucher restored to Assigned status");

        await using var verify = CreateContext();
        var stored = await verify.FuelVouchers
            .Where(v => v.Id == voucherId)
            .Select(v => new { v.Status, v.UsedAtUtc, v.UpdatedAtUtc })
            .SingleAsync();

        stored.Status.Should().Be(VoucherStatus.Assigned);
        stored.UsedAtUtc.Should().BeNull("a restored voucher was not redeemed, so it has no redemption time");
        stored.UpdatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// D-13, and the exact inverse of the redemption event. Undoing a redemption moves no paper
    /// either - the customer never gave the ticket up - so this writes an event and not a
    /// <c>returned</c> movement, and records who undid it.
    /// </summary>
    [Fact]
    public async Task RestoreVoucher_RecordsTheUndo_NamingWhoDidIt()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedUsedVoucherAsync(userId, voucherId);

        await using var context = CreateContext();
        var handler = new RestoreVoucherCommandHandler(context);

        await handler.HandleAsync(new RestoreVoucherCommand(voucherId, userId));

        await using var verify = CreateContext();
        var undo = await verify.VoucherEvents
            .AsNoTracking()
            .Where(e => e.VoucherId == voucherId)
            .Select(e => new { e.EventType, e.FromStatus, e.ToStatus, e.ActorKind, e.ActorUserId })
            .SingleAsync();

        undo.EventType.Should().Be(VoucherEventTypes.StatusChanged);
        undo.FromStatus.Should().Be(nameof(VoucherStatus.Used));
        undo.ToStatus.Should().Be(nameof(VoucherStatus.Assigned));
        undo.ActorKind.Should().Be(VoucherActorKinds.User);
        undo.ActorUserId.Should().Be(userId);
    }

    /// <summary>
    /// The boundary this PR deliberately did not cross. Un-archiving is an operator action, so the
    /// global query filter stays in force: a customer must not be able to bring back a voucher an
    /// admin archived by id - <c>delete_all</c> archives every voucher in the system.
    /// </summary>
    [Fact]
    public async Task RestoreVoucher_RefusesToTouchAnArchivedVoucher()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedUsedVoucherAsync(userId, voucherId, archived: true);

        await using var context = CreateContext();
        var handler = new RestoreVoucherCommandHandler(context);

        var response = await handler.HandleAsync(new RestoreVoucherCommand(voucherId, userId));

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("NotFound");

        await using var verify = CreateContext();
        var stored = await verify.FuelVouchers
            .IgnoreQueryFilters()
            .Where(v => v.Id == voucherId)
            .Select(v => new { v.Status, v.IsDeleted })
            .SingleAsync();

        stored.Status.Should().Be(VoucherStatus.Used, "an archived voucher must come back untouched");
        stored.IsDeleted.Should().BeTrue();
    }

    /// <summary>
    /// Someone else's redeemed voucher. The endpoint used to be Staff-only while its only caller was
    /// a customer's mobile toggle, so ownership was enforced by nothing at all - opening the endpoint
    /// to customers without moving the check into the handler would have handed them the ability to
    /// un-redeem each other's vouchers.
    /// </summary>
    [Fact]
    public async Task RestoreVoucher_RefusesAnotherCustomersVoucher()
    {
        var ownerId = Guid.NewGuid();
        var intruderId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedUsedVoucherAsync(ownerId, voucherId);

        await using var context = CreateContext();
        var handler = new RestoreVoucherCommandHandler(context);

        var response = await handler.HandleAsync(new RestoreVoucherCommand(voucherId, intruderId));

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("Forbidden");

        await using var verify = CreateContext();
        var stored = await verify.FuelVouchers
            .Where(v => v.Id == voucherId)
            .Select(v => v.Status)
            .SingleAsync();

        stored.Should().Be(VoucherStatus.Used);

        // A refusal changes nothing, so it records nothing. An event here would name a person for an
        // action the system just declined to take.
        (await verify.VoucherEvents.CountAsync(e => e.VoucherId == voucherId))
            .Should().Be(0);
    }

    private async Task SeedUsedVoucherAsync(Guid userId, Guid voucherId, bool archived = false)
    {
        await using var seed = CreateContext();
        await ResetDataAsync(seed);

        seed.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        seed.FuelVouchers.Add(new FuelVoucher
        {
            Id = voucherId,
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = $"OKKO-{voucherId:N}"[..20],
            QrPayload = $"payload-{voucherId:N}",
            Status = VoucherStatus.Used,
            AssignedToUserId = userId,
            OrderId = SeedPurchaseOrder(seed, userId),
            UsedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            IsDeleted = archived,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await seed.SaveChangesAsync();
    }

    /// <summary>
    /// The purchase a held voucher came out of. A voucher in somebody's hands must carry an order
    /// (<c>ck_voucher_held_has_order</c>), so every Assigned/Used/Blocked seed needs one behind it.
    /// </summary>
    private static Guid SeedPurchaseOrder(ApplicationDbContext ctx, Guid userId)
    {
        var orderId = Guid.NewGuid();
        ctx.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 0,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        return orderId;
    }

    private static async Task ResetDataAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}