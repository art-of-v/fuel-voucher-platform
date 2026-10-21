using FluentAssertions;
using FuelFlow.Features.Company.BlockWorkerVoucher;
using FuelFlow.Features.Company.FireWorker;
using FuelFlow.Features.Company.GiftVouchers;
using FuelFlow.Features.Company.ReturnVoucherToPool;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The fuel of a worker who no longer works here, and the way back to the company pool
/// (planning #558).
/// <para>
/// Firing a worker splits the fuel they held in two: the vouchers that were still <c>Assigned</c>
/// became company fuel and went back under the purchase, and the ones the owner had already
/// frozen kept the worker link and the handover. The first kind was frozen too, and every door
/// was closed on it - the pool cannot issue a frozen voucher and the thaw wants a worker to thaw
/// it for. The second kind could only be thawed, and a thaw hands the paper back to the person
/// who was just fired.
/// </para>
/// <para>
/// These run against real Postgres because the transition is a compare-and-swap
/// <c>ExecuteUpdateAsync</c> and because the constraints that hold the shape (the holder matrix,
/// the held-voucher-has-an-order rule) are CHECKs an in-memory provider does not model.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class CompanyFiredWorkerFuelIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public CompanyFiredWorkerFuelIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_Frozen_Fuel_Of_A_Fired_Worker_Goes_Back_To_The_Pool_In_One_Step()
    {
        var ownerId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        Guid purchaseOrderId;

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerId));
            seed.LegalEntities.Add(NewLegalEntity(legalEntityId, ownerId));
            seed.CompanyMembers.Add(NewMember(memberId, legalEntityId, workerId));
            purchaseOrderId = SeedPurchaseOrder(seed, ownerId);
            seed.FuelVouchers.Add(
                NewVoucher(voucherId, VoucherStatus.Assigned, legalEntityId, ownerId, workerUserId: null, purchaseOrderId));
            await seed.SaveChangesAsync();
        }

        await using (var giftCtx = CreateContext())
        {
            await new GiftVouchersCommandHandler(giftCtx, new FuelFlowMetrics())
                .HandleAsync(new GiftVouchersCommand(ownerId, workerId, new[] { voucherId }));
        }

        // The owner freezes it, and then the worker is let go. The freeze keeps the worker
        // link, and the firing leaves a frozen voucher alone.
        await using (var freezeCtx = CreateContext())
        {
            var frozen = await new BlockWorkerVoucherCommandHandler(freezeCtx, new FuelFlowMetrics())
                .HandleAsync(new BlockWorkerVoucherCommand(ownerId, voucherId, legalEntityId));

            frozen.Status.Should().Be("Success");
        }

        await using (var fireCtx = CreateContext())
        {
            var result = await new FireWorkerCommandHandler(fireCtx, new FuelFlowMetrics())
                .HandleAsync(new FireWorkerCommand(ownerId, memberId));

            result.Status.Should().Be("Success");
            result.BlockedVoucherCount.Should().Be(0, "a frozen voucher is not the firing's business");
        }

        await using (var returnCtx = CreateContext())
        {
            var result = await new ReturnVoucherToPoolCommandHandler(returnCtx, new FuelFlowMetrics())
                .HandleAsync(new ReturnVoucherToPoolCommand(ownerId, voucherId, legalEntityId));

            result.Status.Should().Be("Success");
        }

        await using var verify = CreateContext();
        var returned = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucherId);

        // Company fuel again, and spendable: the pool can issue it to whoever it likes.
        returned.WorkerUserId.Should().BeNull();
        returned.Status.Should().Be(VoucherStatus.Assigned);
        returned.OrderId.Should().Be(purchaseOrderId, "the fuel belongs to the purchase it arrived with");

        // The person who was just fired can no longer redeem it.
        VoucherHolder.CanRedeem(returned.WorkerUserId, returned.AssignedToUserId, workerId).Should().BeFalse();

        // D-13. The paper left the worker's hands, so a movement says so - and it names the
        // handover it undoes, not the purchase the voucher went back to.
        var movements = await verify.VoucherMovements.AsNoTracking()
            .Where(m => m.VoucherId == voucherId)
            .OrderBy(m => m.OccurredAtUtc)
            .ToListAsync();

        movements.Should().HaveCount(2, "the gift moved the paper out and the return moved it back");
        movements[0].Kind.Should().Be(VoucherMovementKinds.Gift);
        movements[1].Kind.Should().Be(VoucherMovementKinds.Recall);
        movements[1].FromLocation.Should().Be(VoucherLocations.Worker);
        movements[1].ToLocation.Should().Be(VoucherLocations.Company);
        movements[1].ActorUserId.Should().Be(ownerId);
        movements[1].OrderId.Should().NotBe(purchaseOrderId);

        // And the status changed, which is what the movement cannot say: the voucher stopped
        // being frozen. The freeze wrote the other half of the pair.
        var events = await verify.VoucherEvents.AsNoTracking()
            .Where(e => e.VoucherId == voucherId && e.EventType == VoucherEventTypes.StatusChanged)
            .ToListAsync();

        events.Should().ContainSingle(e =>
            e.FromStatus == nameof(VoucherStatus.Blocked)
            && e.ToStatus == nameof(VoucherStatus.Assigned));
    }

    [Fact]
    public async Task The_Fuel_A_Firing_Froze_On_Its_Way_Out_Can_Be_Issued_Again()
    {
        // The other half of a firing: the vouchers that were still active became company
        // fuel and frozen at the same moment. They had no door open at all - the pool cannot
        // issue a frozen voucher - and their litres were counted as the company's stock.
        var ownerId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var otherWorkerId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var otherMemberId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        Guid purchaseOrderId;

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerId), NewUser(otherWorkerId));
            seed.LegalEntities.Add(NewLegalEntity(legalEntityId, ownerId));
            seed.CompanyMembers.Add(NewMember(memberId, legalEntityId, workerId));
            seed.CompanyMembers.Add(NewMember(otherMemberId, legalEntityId, otherWorkerId));
            purchaseOrderId = SeedPurchaseOrder(seed, ownerId);
            seed.FuelVouchers.Add(
                NewVoucher(voucherId, VoucherStatus.Assigned, legalEntityId, ownerId, workerUserId: null, purchaseOrderId));
            await seed.SaveChangesAsync();
        }

        await using (var giftCtx = CreateContext())
        {
            await new GiftVouchersCommandHandler(giftCtx, new FuelFlowMetrics())
                .HandleAsync(new GiftVouchersCommand(ownerId, workerId, new[] { voucherId }));
        }

        await using (var fireCtx = CreateContext())
        {
            var result = await new FireWorkerCommandHandler(fireCtx, new FuelFlowMetrics())
                .HandleAsync(new FireWorkerCommand(ownerId, memberId));

            result.Status.Should().Be("Success");
            result.BlockedVoucherCount.Should().Be(1);
        }

        await using (var before = CreateContext())
        {
            // The gift moved the paper out to the worker and the firing moved it back to the
            // company - that is the movement the firing owes, and it is the last one.
            (await before.VoucherMovements.CountAsync(m => m.VoucherId == voucherId)).Should().Be(2);
        }

        await using (var returnCtx = CreateContext())
        {
            var result = await new ReturnVoucherToPoolCommandHandler(returnCtx, new FuelFlowMetrics())
                .HandleAsync(new ReturnVoucherToPoolCommand(ownerId, voucherId, legalEntityId));

            result.Status.Should().Be("Success");
        }

        await using var verify = CreateContext();
        var returned = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucherId);

        returned.WorkerUserId.Should().BeNull();
        returned.Status.Should().Be(VoucherStatus.Assigned);
        returned.OrderId.Should().Be(purchaseOrderId);

        // No third movement: the paper was already at the company - the firing took it back -
        // so the return changed what the voucher IS, not where it is.
        var movements = await verify.VoucherMovements.AsNoTracking()
            .Where(m => m.VoucherId == voucherId)
            .ToListAsync();

        movements.Should().HaveCount(2, "the firing moved the paper; the return did not");
        movements.Should().OnlyContain(m => m.Kind == VoucherMovementKinds.Gift || m.Kind == VoucherMovementKinds.Recall);

        var events = await verify.VoucherEvents.AsNoTracking()
            .Where(e => e.VoucherId == voucherId && e.EventType == VoucherEventTypes.StatusChanged)
            .ToListAsync();

        events.Should().ContainSingle(e =>
            e.FromStatus == nameof(VoucherStatus.Blocked)
            && e.ToStatus == nameof(VoucherStatus.Assigned));

        // And now it moves: the pool can issue it to somebody else.
        await using (var issueCtx = CreateContext())
        {
            var issued = await new GiftVouchersCommandHandler(issueCtx, new FuelFlowMetrics())
                .HandleAsync(new GiftVouchersCommand(ownerId, otherWorkerId, new[] { voucherId }));

            issued.Status.Should().Be("Success");
            issued.GiftedCount.Should().Be(1);
        }

        await using (var afterIssue = CreateContext())
        {
            var issued = await afterIssue.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucherId);
            issued.WorkerUserId.Should().Be(otherWorkerId);
            issued.Status.Should().Be(VoucherStatus.Assigned);
        }
    }

    [Fact]
    public async Task The_Frozen_Fuel_Of_A_Worker_Who_Still_Works_Here_Can_Go_Back_To_The_Pool()
    {
        // The same rule, the other way round: the owner may take a frozen voucher back from
        // a worker who still works here, in one step, instead of thawing it and then recalling
        // it - which left a window in which the worker could spend it.
        var ownerId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        Guid purchaseOrderId;

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerId));
            seed.LegalEntities.Add(NewLegalEntity(legalEntityId, ownerId));
            seed.CompanyMembers.Add(NewMember(memberId, legalEntityId, workerId));
            purchaseOrderId = SeedPurchaseOrder(seed, ownerId);
            seed.FuelVouchers.Add(
                NewVoucher(voucherId, VoucherStatus.Assigned, legalEntityId, ownerId, workerUserId: null, purchaseOrderId));
            await seed.SaveChangesAsync();
        }

        await using (var giftCtx = CreateContext())
        {
            await new GiftVouchersCommandHandler(giftCtx, new FuelFlowMetrics())
                .HandleAsync(new GiftVouchersCommand(ownerId, workerId, new[] { voucherId }));
        }

        await using (var freezeCtx = CreateContext())
        {
            var frozen = await new BlockWorkerVoucherCommandHandler(freezeCtx, new FuelFlowMetrics())
                .HandleAsync(new BlockWorkerVoucherCommand(ownerId, voucherId, legalEntityId));

            frozen.Status.Should().Be("Success");
        }

        await using (var returnCtx = CreateContext())
        {
            var result = await new ReturnVoucherToPoolCommandHandler(returnCtx, new FuelFlowMetrics())
                .HandleAsync(new ReturnVoucherToPoolCommand(ownerId, voucherId, legalEntityId));

            result.Status.Should().Be("Success");
        }

        await using var verify = CreateContext();
        var returned = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucherId);

        returned.WorkerUserId.Should().BeNull();
        returned.Status.Should().Be(VoucherStatus.Assigned);
        returned.OrderId.Should().Be(purchaseOrderId);
    }

    [Fact]
    public async Task A_Voucher_That_Is_Not_Frozen_Cannot_Go_Back_To_The_Pool()
    {
        var ownerId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerId));
            seed.LegalEntities.Add(NewLegalEntity(legalEntityId, ownerId));
            seed.CompanyMembers.Add(NewMember(memberId, legalEntityId, workerId));
            var orderId = SeedPurchaseOrder(seed, ownerId);
            seed.FuelVouchers.Add(
                NewVoucher(voucherId, VoucherStatus.Assigned, legalEntityId, ownerId, workerUserId: null, orderId));
            await seed.SaveChangesAsync();
        }

        await using (var giftCtx = CreateContext())
        {
            await new GiftVouchersCommandHandler(giftCtx, new FuelFlowMetrics())
                .HandleAsync(new GiftVouchersCommand(ownerId, workerId, new[] { voucherId }));
        }

        await using (var act = CreateContext())
        {
            var result = await new ReturnVoucherToPoolCommandHandler(act, new FuelFlowMetrics())
                .HandleAsync(new ReturnVoucherToPoolCommand(ownerId, voucherId, legalEntityId));

            result.Status.Should().Be("InvalidState");
        }

        await using var verify = CreateContext();
        var untouched = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucherId);

        untouched.WorkerUserId.Should().Be(workerId);
        untouched.Status.Should().Be(VoucherStatus.Assigned);

        (await verify.VoucherMovements.CountAsync(m => m.VoucherId == voucherId))
            .Should().Be(1, "only the gift moved it; a refused action moves nothing");
    }

    // ── seed builders (mirror FuelFlow.UnitTests.Company.CompanyTestFactory) ──────────────────
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

    private static CompanyMember NewMember(Guid id, Guid legalEntityId, Guid workerUserId)
        => new()
        {
            Id = id,
            LegalEntityId = legalEntityId,
            WorkerUserId = workerUserId,
            JoinedAtUtc = DateTime.UtcNow
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
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
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
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "company_invitations", "company_members", "legal_entities", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
