using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.MarkVoucherAsUsed;
using FuelFlow.Features.Vouchers.Redemptions;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Covers WP-4 (money-integrity review; conclusions now in <c>docs/SECURITY.md (private docs repo)</c>,
/// "Fraud analysis findings"): <see cref="MarkVoucherAsUsedCommandHandler"/> used to
/// read the voucher, check its status, and then write — so two concurrent redemptions could both
/// observe <see cref="VoucherStatus.Assigned"/> and both write <see cref="VoucherStatus.Used"/>.
///
/// These tests live in the integration suite rather than the unit suite deliberately: the fix is an
/// <c>ExecuteUpdateAsync</c> whose WHERE clause carries the status predicate, and the EF Core
/// in-memory brand neither translates <c>ExecuteUpdate</c> nor models row locking. A concurrency
/// guard cannot be verified against a brand that has no concurrency.
/// </summary>
[Collection("Integration Tests")]
public sealed class MarkVoucherAsUsedConcurrencyIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public MarkVoucherAsUsedConcurrencyIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Moved here from <c>FuelFlow.UnitTests.Vouchers.VoucherCommandHandlersTests</c>, which could no
    /// longer execute it once the write became an <c>ExecuteUpdateAsync</c>.
    /// </summary>
    [Fact]
    public async Task MarkVoucherAsUsed_TransitionsFromAssignedToPendingReconciliation()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedAssignedVoucherAsync(userId, voucherId);

        await using var context = CreateContext();
        var handler = new MarkVoucherAsUsedCommandHandler(context, TestRedemptions.For(context), NullLogger<MarkVoucherAsUsedCommandHandler>.Instance);

        var response = await handler.HandleAsync(new MarkVoucherAsUsedCommand(voucherId, userId));

        response.Success.Should().BeTrue();
        response.Message.Should().Be("Voucher reported; waiting for supplier confirmation");

        await using var verify = CreateContext();
        var stored = await verify.FuelVouchers
            .Where(v => v.Id == voucherId)
            .Select(v => new { v.Status, v.UsedAtUtc, v.UpdatedAtUtc })
            .SingleAsync();

        stored.Status.Should().Be(VoucherStatus.PendingReconciliation);
        stored.UsedAtUtc.Should().BeNull("the customer reported the fuel, not the supplier's evidence");
        stored.UpdatedAtUtc.Should().BeCloseTo(TestClock.UtcNow, TimeSpan.FromSeconds(10));

        var redemption = await verify.VoucherRedemptions
            .Where(r => r.VoucherId == voucherId)
            .SingleAsync();
        redemption.Status.Should().Be(VoucherRedemptionStatus.Reported);
        redemption.ReportedByUserId.Should().Be(userId);
        redemption.ReportedAtUtc.Should().BeCloseTo(TestClock.UtcNow, TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// D-13, fifth writer. A redemption used to be invisible in the record: the status moved and
    /// nothing said so, which left the one action a customer actually performs as the one action with
    /// no author and no timestamp anywhere.
    /// <para>
    /// It is an event and not a movement, and that is the model's own distinction rather than a
    /// preference: the paper does not change hands, it is spent in the customer's hand.
    /// </para>
    /// </summary>
    [Fact]
    public async Task MarkVoucherAsUsed_RecordsTheRedemption_NamingWhoDidIt()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedAssignedVoucherAsync(userId, voucherId);

        await using var context = CreateContext();
        var handler = new MarkVoucherAsUsedCommandHandler(context, TestRedemptions.For(context), NullLogger<MarkVoucherAsUsedCommandHandler>.Instance);

        await handler.HandleAsync(new MarkVoucherAsUsedCommand(voucherId, userId));

        await using var verify = CreateContext();
        var reported = await verify.VoucherEvents
            .AsNoTracking()
            .Where(e => e.VoucherId == voucherId)
            .Select(e => new { e.EventType, e.FromStatus, e.ToStatus, e.ActorKind, e.ActorUserId, e.OccurredAtUtc })
            .SingleAsync();
        var claim = await verify.VoucherRedemptions
            .AsNoTracking()
            .Where(r => r.VoucherId == voucherId)
            .Select(r => new { r.ReportedAtUtc })
            .SingleAsync();

        reported.EventType.Should().Be(VoucherEventTypes.StatusChanged);
        reported.FromStatus.Should().Be(nameof(VoucherStatus.Assigned));
        reported.ToStatus.Should().Be(nameof(VoucherStatus.PendingReconciliation));
        reported.ActorKind.Should().Be(VoucherActorKinds.User);
        reported.ActorUserId.Should().Be(userId);
        reported.OccurredAtUtc.Should().Be(claim.ReportedAtUtc,
            "the event and the claim are the same instant - one moment, two tables");
    }

    /// <summary>
    /// The idempotent branch must stay silent. Marking an already-marked voucher is the same handover
    /// seen twice, not two redemptions, and an event per call would put a row on one tank of fuel.
    /// </summary>
    [Fact]
    public async Task MarkVoucherAsUsed_Twice_RecordsOneEvent()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedAssignedVoucherAsync(userId, voucherId);

        await using (var first = CreateContext())
        {
            var handler = new MarkVoucherAsUsedCommandHandler(first, TestRedemptions.For(first), NullLogger<MarkVoucherAsUsedCommandHandler>.Instance);
            (await handler.HandleAsync(new MarkVoucherAsUsedCommand(voucherId, userId))).Success
                .Should().BeTrue();
        }

        await using (var second = CreateContext())
        {
            var handler = new MarkVoucherAsUsedCommandHandler(second, TestRedemptions.For(second), NullLogger<MarkVoucherAsUsedCommandHandler>.Instance);
            (await handler.HandleAsync(new MarkVoucherAsUsedCommand(voucherId, userId))).Message
                .Should().Be("Voucher is already awaiting supplier confirmation");
        }

        await using var verify = CreateContext();
        (await verify.VoucherEvents.CountAsync(e => e.VoucherId == voucherId))
            .Should().Be(1, "a second mark-as-used is the same report, not a new one");
        (await verify.VoucherRedemptions.CountAsync(r => r.VoucherId == voucherId))
            .Should().Be(1, "a second mark-as-used must not open a second claim");
    }

    /// <summary>
    /// The regression test for WP-4, made deterministic without a seam in production code.
    ///
    /// A holder transaction redeems the voucher and does not commit, so it keeps the row lock. The
    /// handler's pre-check read is an MVCC snapshot read and still sees <c>Assigned</c>; its
    /// conditional UPDATE then blocks on that lock. Once the holder commits, Postgres re-evaluates
    /// the UPDATE's WHERE clause against the newly committed row (READ COMMITTED), the status
    /// predicate no longer matches, and 0 rows are affected — so the handler reports the idempotent
    /// outcome instead of blindly overwriting.
    ///
    /// Against the pre-fix read-then-write code this test fails: that UPDATE carried no status
    /// predicate, so it would unblock, write unconditionally, and report "Voucher marked as used".
    /// </summary>
    [Fact]
    public async Task ConcurrentRedemption_TransitionsExactlyOnce()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedAssignedVoucherAsync(userId, voucherId);

        await using var holder = CreateContext();
        await using var holderTransaction = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlRawAsync(
            "UPDATE fuel_vouchers SET status = 'Used' WHERE id = {0}",
            voucherId);

        await using var handlerContext = CreateContext();
        var handler = new MarkVoucherAsUsedCommandHandler(handlerContext, TestRedemptions.For(handlerContext), NullLogger<MarkVoucherAsUsedCommandHandler>.Instance);
        var redemption = Task.Run(() => handler.HandleAsync(new MarkVoucherAsUsedCommand(voucherId, userId)));

        // Long enough for the handler to clear its pre-check and reach the UPDATE, where it parks
        // on the holder's row lock. If it has finished by now it never contended at all and the
        // test would be proving nothing.
        await Task.Delay(TimeSpan.FromSeconds(2));
        redemption.IsCompleted.Should().BeFalse(
            "the conditional UPDATE must block on the row lock held by the uncommitted holder");

        await holderTransaction.CommitAsync();

        var response = await redemption;

        response.Success.Should().BeTrue("redemption of an already-used voucher is idempotent");
        response.Message.Should().Be("Voucher already marked as used");

        await using var verify = CreateContext();
        var status = await verify.FuelVouchers
            .Where(v => v.Id == voucherId)
            .Select(v => v.Status)
            .SingleAsync();

        status.Should().Be(VoucherStatus.Used);

        // The loser of the race performed no transition, so it may claim none. Without this the CAS
        // would still be correct and the record would not be: two callers, one redemption, and a
        // second row attributing a handover that never happened.
        await using var verifyEvents = CreateContext();
        (await verifyEvents.VoucherEvents.CountAsync(e => e.VoucherId == voucherId))
            .Should().Be(0, "the handler that lost the CAS did not redeem anything, so it records nothing");
        (await verifyEvents.VoucherRedemptions.CountAsync(r => r.VoucherId == voucherId))
            .Should().Be(0, "the loser of the race opened no claim either");
    }

    private async Task SeedAssignedVoucherAsync(Guid userId, Guid voucherId)
    {
        await using var seed = CreateContext();
        await seed.Database.MigrateAsync();
        await ResetDataAsync(seed);

        seed.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        seed.FuelVouchers.Add(new FuelVoucher
        {
            Id = voucherId,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
            VoucherNumber = $"OKKO-{voucherId:N}"[..20],
            QrPayload = $"payload-{voucherId:N}",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            OrderId = SeedPurchaseOrder(seed, userId),
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
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
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        return orderId;
    }

    private static async Task ResetDataAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                "journal_lines", "journal_entries", "document_sequences", "accounting_periods",
                "voucher_redemptions", "redemption_statement_rows",
                "voucher_events", "voucher_movements", "order_line_items", "orders",
                "fuel_vouchers", "users", "payments", "fulfillments", "outbox_events",
                "provider_event_outbox", "app_settings"
            RESTART IDENTITY CASCADE
            """);

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
