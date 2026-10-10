using FluentAssertions;
using FuelFlow.Features.Company.BlockWorkerVoucher;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Company.UnblockWorkerVoucher;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.GetUserVouchers;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Epic #103 S3b real-Postgres coverage for the owner block/unblock toggle on a worker-held voucher
/// (<see cref="BlockWorkerVoucherCommandHandler"/> / <see cref="UnblockWorkerVoucherCommandHandler"/>).
/// These live in the integration suite because the state transition is an atomic
/// <c>ExecuteUpdateAsync</c> whose WHERE repeats the status predicate — the InMemory provider neither
/// translates <c>ExecuteUpdate</c> nor models the row locking the block-vs-redeem race depends on.
/// The pure ownership/scoping/state guards are unit-tested in
/// <c>FuelFlow.UnitTests.Company.CompanyVoucherBlockingTests</c>.
///
/// Pattern A (handler-direct, raw context) — mirrors <see cref="CompanyMembershipIntegrationTests"/>.
/// </summary>
[Collection("Integration Tests")]
public sealed class CompanyVoucherBlockingIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public CompanyVoucherBlockingIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Block_FreezesWorkerVoucher_KeepingTheWorkerLink_Durably()
    {
        var ownerId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerId));
            seed.LegalEntities.Add(NewLegalEntity(legalEntityId, ownerId));
            var orderId = SeedPurchaseOrder(seed, ownerId);
            seed.FuelVouchers.Add(NewVoucher(voucherId, VoucherStatus.Assigned, legalEntityId, ownerId, workerId, orderId));
            await seed.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            var result = await new BlockWorkerVoucherCommandHandler(act, new FuelFlowMetrics())
                .HandleAsync(new BlockWorkerVoucherCommand(ownerId, voucherId, legalEntityId));
            result.Status.Should().Be("Success");
        }

        await using var verify = CreateContext();
        var stored = await verify.FuelVouchers.SingleAsync(v => v.Id == voucherId);
        stored.Status.Should().Be(VoucherStatus.Blocked);
        stored.WorkerUserId.Should().Be(workerId, "block freezes the voucher but keeps it with the worker");
    }

    [Fact]
    public async Task Unblock_ThawsBlockedWorkerVoucher_KeepingTheWorkerLink_Durably()
    {
        var ownerId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerId));
            seed.LegalEntities.Add(NewLegalEntity(legalEntityId, ownerId));
            var orderId = SeedPurchaseOrder(seed, ownerId);
            seed.FuelVouchers.Add(NewVoucher(voucherId, VoucherStatus.Blocked, legalEntityId, ownerId, workerId, orderId));
            await seed.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            var result = await new UnblockWorkerVoucherCommandHandler(act, new FuelFlowMetrics())
                .HandleAsync(new UnblockWorkerVoucherCommand(ownerId, voucherId, legalEntityId));
            result.Status.Should().Be("Success");
        }

        await using var verify = CreateContext();
        var stored = await verify.FuelVouchers.SingleAsync(v => v.Id == voucherId);
        stored.Status.Should().Be(VoucherStatus.Assigned);
        stored.WorkerUserId.Should().Be(workerId, "unblock returns the voucher to redeemable, still with the worker");
    }

    [Fact]
    public async Task Block_FallsBackToOldestEntity_WhenIdOmitted()
    {
        var ownerId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var olderEntityId = Guid.NewGuid();
        var newerEntityId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerId));
            var older = NewLegalEntity(olderEntityId, ownerId, edrpou: "10000001");
            older.CreatedAtUtc = DateTime.UtcNow.AddDays(-10);
            var newer = NewLegalEntity(newerEntityId, ownerId, edrpou: "10000002");
            newer.CreatedAtUtc = DateTime.UtcNow.AddDays(-1);
            seed.LegalEntities.AddRange(older, newer);
            // Voucher lives under the OLDER entity (the omitted-id fallback target).
            var orderId = SeedPurchaseOrder(seed, ownerId);
            seed.FuelVouchers.Add(NewVoucher(voucherId, VoucherStatus.Assigned, olderEntityId, ownerId, workerId, orderId));
            await seed.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            var result = await new BlockWorkerVoucherCommandHandler(act, new FuelFlowMetrics())
                .HandleAsync(new BlockWorkerVoucherCommand(ownerId, voucherId));
            result.Status.Should().Be("Success");
        }

        await using var verify = CreateContext();
        (await verify.FuelVouchers.SingleAsync(v => v.Id == voucherId)).Status.Should().Be(VoucherStatus.Blocked);
    }

    [Fact]
    public async Task GetUserVouchers_ShowsBlockedVoucherToOwner_ButHidesItFromWorker()
    {
        var ownerId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerId));
            seed.LegalEntities.Add(NewLegalEntity(legalEntityId, ownerId));
            var orderId = SeedPurchaseOrder(seed, ownerId);
            seed.FuelVouchers.Add(NewVoucher(voucherId, VoucherStatus.Blocked, legalEntityId, ownerId, workerId, orderId));
            await seed.SaveChangesAsync();
        }

        await using var query = CreateContext();
        var handler = new GetUserVouchersCommandHandler(
            query, new StubQrGenerator(), NullLogger<GetUserVouchersCommandHandler>.Instance);

        var ownerView = await handler.HandleAsync(new GetUserVouchersCommand(ownerId));
        ownerView.Vouchers.Should().ContainSingle(v => v.Id == voucherId,
            "the owner must see a voucher they froze so they can unblock it");

        var workerView = await handler.HandleAsync(new GetUserVouchersCommand(workerId));
        workerView.Vouchers.Should().NotContain(v => v.Id == voucherId,
            "a blocked voucher drops out of the worker's wallet until the owner lifts the freeze");
    }

    /// <summary>
    /// The race guard: a worker redemption and an owner block observe the same Assigned snapshot.
    /// A holder transaction redeems (Used) without committing, keeping the row lock; the block
    /// handler clears its pre-check (snapshot still Assigned) and parks on the conditional UPDATE.
    /// Once the holder commits, Postgres re-evaluates the block's WHERE (Status == Assigned) against
    /// the committed Used row, matches 0 rows, and the block reports InvalidState rather than
    /// clobbering a redeemed voucher. Mirrors <see cref="MarkVoucherAsUsedConcurrencyIntegrationTests"/>.
    /// </summary>
    [Fact]
    public async Task Block_LosesToAConcurrentRedemption_WithoutClobberingUsed()
    {
        var ownerId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerId));
            seed.LegalEntities.Add(NewLegalEntity(legalEntityId, ownerId));
            var orderId = SeedPurchaseOrder(seed, ownerId);
            seed.FuelVouchers.Add(NewVoucher(voucherId, VoucherStatus.Assigned, legalEntityId, ownerId, workerId, orderId));
            await seed.SaveChangesAsync();
        }

        await using var holder = CreateContext();
        await using var holderTransaction = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlRawAsync(
            "UPDATE fuel_vouchers SET status = 'Used' WHERE id = {0}", voucherId);

        await using var handlerContext = CreateContext();
        var handler = new BlockWorkerVoucherCommandHandler(handlerContext, new FuelFlowMetrics());
        var block = Task.Run(() => handler.HandleAsync(new BlockWorkerVoucherCommand(ownerId, voucherId, legalEntityId)));

        // Long enough for the handler to clear its pre-check and park on the holder's row lock.
        await Task.Delay(TimeSpan.FromSeconds(2));
        block.IsCompleted.Should().BeFalse(
            "the conditional UPDATE must block on the row lock held by the uncommitted redemption");

        await holderTransaction.CommitAsync();

        var result = await block;
        result.Status.Should().Be("InvalidState", "a voucher already redeemed can no longer be blocked");

        await using var verify = CreateContext();
        var status = await verify.FuelVouchers.Where(v => v.Id == voucherId).Select(v => v.Status).SingleAsync();
        status.Should().Be(VoucherStatus.Used, "the redemption must stand; block must not overwrite it");
    }

    // ── seed builders (mirror CompanyMembershipIntegrationTests) ──────────────────────────────

    private sealed class StubQrGenerator : IQrGenerator
    {
        public string GenerateQrCode(string payload, int width = 300, int height = 300,
            string? eccLevel = null, int? version = null, string? encodingMode = null, int? maskPattern = null)
            => "stub-qr";
    }

    private static User NewUser(Guid id)
        => new()
        {
            Id = id,
            PhoneNumber = $"+38{id:N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static LegalEntity NewLegalEntity(Guid id, Guid ownerUserId, string name = "ACME LLC", string edrpou = "12345678")
        => new()
        {
            Id = id,
            UserId = ownerUserId,
            Name = name,
            Edrpou = edrpou,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static FuelVoucher NewVoucher(
        Guid id,
        VoucherStatus status,
        Guid? legalEntityId = null,
        Guid? assignedToUserId = null,
        Guid? workerUserId = null,
        Guid? orderId = null)
        => new()
        {
            Id = id,
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50m,
            ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = $"OKKO-{id:N}"[..20],
            QrPayload = $"payload-{id:N}",
            Status = status,
            LegalEntityId = legalEntityId,
            AssignedToUserId = assignedToUserId,
            WorkerUserId = workerUserId,
            OrderId = orderId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

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

    private static async Task ResetAsync(ApplicationDbContext context)
    {
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "refunds", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "company_invitations", "company_members", "legal_entities", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}
