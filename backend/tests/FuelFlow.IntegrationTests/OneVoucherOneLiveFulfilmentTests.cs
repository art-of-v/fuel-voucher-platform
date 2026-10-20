using FluentAssertions;
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
/// <b>One voucher, one live fulfilment</b> - and the database is what says so.
/// </summary>
/// <remarks>
/// <para>
/// The claim is atomic in the application: <c>TryAssignVoucherAsync</c> compares and swaps the
/// fulfilment row, so two workers cannot take the same voucher. The application is also the
/// thing that can be wrong - a second live fulfilment for one voucher hands the same bearer
/// instrument to two customers, and a voucher is money.
/// </para>
/// <para>
/// So the invariant moved into the database, as a partial unique index over the live rows:
/// <c>ux_fulfillments_one_live_per_voucher</c> on <c>(voucher_id) WHERE released_at_utc IS
/// NULL</c>. The index is partial on the same predicate as the entity's global query filter,
/// so it covers exactly the rows that are live - every row EF can see, and every row the raw
/// claim statement touches.
/// </para>
/// <para>
/// The two tests are the two halves of the invariant. The first is that a second LIVE
/// fulfilment is refused; the second is that a RELEASED one does not block a later live one,
/// because a released fulfilment is a record of what happened and not a claim on the voucher.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class OneVoucherOneLiveFulfilmentTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public OneVoucherOneLiveFulfilmentTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// The invariant: the database refuses a second live fulfilment for a voucher that already
    /// has one. The application would refuse it too - this is what happens when the
    /// application does not.
    /// </summary>
    [Fact]
    public async Task The_Database_Refuses_A_Second_Live_Fulfilment_For_One_Voucher()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetDataAsync(seed);

            seed.Users.Add(User(userId));
            seed.Orders.Add(SingleLineOrder(orderId, userId, lineId));
            seed.FuelVouchers.Add(Voucher(voucherId, userId, orderId));
            seed.Fulfillments.Add(Fulfilment(orderId, lineId, voucherId));

            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();

        // The second live fulfilment for the same voucher.
        context.Fulfillments.Add(Fulfilment(orderId, lineId, voucherId));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "one voucher has one live fulfilment, and the database is the one that says so");
    }

    /// <summary>
    /// The other half: a RELEASED fulfilment is a record of what happened, not a claim on the
    /// voucher. A voucher that was issued and released can be issued again - otherwise a
    /// released fulfilment would freeze the voucher for ever, and the partial index would be
    /// a bug rather than an invariant.
    /// </summary>
    [Fact]
    public async Task A_Released_Fulfilment_Does_Not_Block_A_Later_Live_One()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetDataAsync(seed);

            seed.Users.Add(User(userId));
            seed.Orders.Add(SingleLineOrder(orderId, userId, lineId));
            seed.FuelVouchers.Add(Voucher(voucherId, userId, orderId));

            var released = Fulfilment(orderId, lineId, voucherId);
            released.ReleasedAtUtc = DateTime.UtcNow;
            seed.Fulfillments.Add(released);

            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();

        // A new live fulfilment for the same voucher: allowed, because the previous one was
        // released.
        context.Fulfillments.Add(Fulfilment(orderId, lineId, voucherId));
        await context.SaveChangesAsync();

        var live = await context.Fulfillments
            .IgnoreQueryFilters()
            .CountAsync(f => f.VoucherId == voucherId && f.ReleasedAtUtc == null);
        live.Should().Be(1, "the released one is a record and the live one is the claim");
    }

    private static async Task ResetDataAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"fulfillments\", \"fuel_vouchers\", \"order_line_items\", \"orders\", \"users\" CASCADE;");
    }

    private static Order SingleLineOrder(Guid orderId, Guid userId, Guid lineId) => new()
    {
        Id = orderId,
        UserId = userId,
        Price = 2500m,
        Status = OrderStatus.PendingFulfillment,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        LineItems = new List<OrderLineItem>
        {
            new OrderLineItem
            {
                Id = lineId,
                OrderId = orderId,
                Brand = "okko",
                FuelTypeId = ToUuid("okko-95"),
                Liters = 10m,
                Quantity = 1,
                UnitPrice = 2500m,
                LineTotal = 2500m
            }
        }
    };

    private static FuelVoucher Voucher(Guid id, Guid userId, Guid orderId)
    {
        var nonce = $"v-{Guid.NewGuid():N}";
        return new FuelVoucher
        {
            Id = id,
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 10m,
            SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            VoucherNumber = nonce,
            QrPayload = nonce,
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            OrderId = orderId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static Fulfillment Fulfilment(Guid orderId, Guid lineId, Guid voucherId) => new()
    {
        OrderId = orderId,
        VoucherId = voucherId,
        LineItemId = lineId,
        FulfilledAtUtc = DateTime.UtcNow
    };

    private static User User(Guid id) => new()
    {
        Id = id,
        PhoneNumber = $"+38{id:N}"[..20],
        FirstName = "Test",
        LastName = "User",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
}
