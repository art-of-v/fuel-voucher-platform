using FluentAssertions;
using FuelFlow.Features.Company.BlockWorkerVoucher;
using FuelFlow.Features.Company.FireWorker;
using FuelFlow.Features.Company.GetOwnerInvitations;
using FuelFlow.Features.Company.GiftVouchers;
using FuelFlow.Features.Company.RecallVoucher;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Real-Postgres coverage for the Company/B2B write handlers, complementing the InMemory unit tests
/// in <c>FuelFlow.UnitTests.Company</c> (PR #722). InMemory has two blind spots this suite closes:
///
/// 1. <b>Transactions are no-ops.</b> <see cref="FireWorkerCommandHandler"/> commits its multi-entity
///    write (block gifted vouchers + remove the member) inside a real
///    <c>BeginTransactionAsync</c>/<c>CommitAsync</c>; the unit test suppresses
///    <c>TransactionIgnoredWarning</c> and so never exercises the real commit path.
/// 2. <b>No constraints, no NoTracking nuance.</b> InMemory ignores the unique <c>legal_entities.edrpou</c>
///    index and doesn't model the NoTracking → explicit-<c>Update()</c> write path the way Postgres does.
///
/// Pattern A (handler-direct, raw context) — mirrors <see cref="MarkVoucherAsUsedConcurrencyIntegrationTests"/>.
/// </summary>
[Collection("Integration Tests")]
public sealed class CompanyMembershipIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public CompanyMembershipIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FireWorker_CommitsBlockedVouchersAndMemberRemoval_Durably()
    {
        var ownerId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var otherWorkerId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var gifted1 = Guid.NewGuid();
        var gifted2 = Guid.NewGuid();
        var usedVoucher = Guid.NewGuid();
        var otherWorkerVoucher = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerId), NewUser(otherWorkerId));
            seed.LegalEntities.Add(NewLegalEntity(legalEntityId, ownerId));
            // Only the fired worker is a member: company_members has a unique index on worker_user_id,
            // so the "other worker" exists purely as an FK target for its own voucher, not as a member.
            seed.CompanyMembers.Add(NewMember(memberId, legalEntityId, workerId));
            // One purchase paid for the whole batch, so all four vouchers share a single order.
            var orderId = SeedPurchaseOrder(seed, ownerId);
            seed.FuelVouchers.AddRange(
                NewVoucher(gifted1, VoucherStatus.Assigned, legalEntityId, ownerId, workerId, orderId),
                NewVoucher(gifted2, VoucherStatus.Assigned, legalEntityId, ownerId, workerId, orderId),
                NewVoucher(usedVoucher, VoucherStatus.Used, legalEntityId, ownerId, workerId, orderId),
                NewVoucher(otherWorkerVoucher, VoucherStatus.Assigned, legalEntityId, ownerId, otherWorkerId, orderId));
            await seed.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            var handler = new FireWorkerCommandHandler(act, new FuelFlowMetrics());
            var result = await handler.HandleAsync(new FireWorkerCommand(ownerId, memberId));

            result.Status.Should().Be("Success");
            result.BlockedVoucherCount.Should().Be(2);
        }

        // Fresh context — proves the transaction actually committed, not just tracked in memory.
        await using var verify = CreateContext();
        var vouchers = await verify.FuelVouchers
            .IgnoreQueryFilters()
            .Where(v => v.LegalEntityId == legalEntityId)
            .ToDictionaryAsync(v => v.Id);

        vouchers[gifted1].Status.Should().Be(VoucherStatus.Blocked);
        vouchers[gifted1].WorkerUserId.Should().BeNull();
        vouchers[gifted2].Status.Should().Be(VoucherStatus.Blocked);
        vouchers[gifted2].WorkerUserId.Should().BeNull();
        vouchers[usedVoucher].Status.Should().Be(VoucherStatus.Used);
        vouchers[usedVoucher].WorkerUserId.Should().Be(workerId);
        vouchers[otherWorkerVoucher].Status.Should().Be(VoucherStatus.Assigned);
        vouchers[otherWorkerVoucher].WorkerUserId.Should().Be(otherWorkerId);

        (await verify.CompanyMembers.AnyAsync(m => m.Id == memberId)).Should().BeFalse();
    }

    [Fact]
    public async Task FireWorker_RemovesMember_WhenNoAssignedVouchers_Durably()
    {
        var ownerId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerId));
            seed.LegalEntities.Add(NewLegalEntity(legalEntityId, ownerId));
            seed.CompanyMembers.Add(NewMember(memberId, legalEntityId, workerId));
            await seed.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            var handler = new FireWorkerCommandHandler(act, new FuelFlowMetrics());
            var result = await handler.HandleAsync(new FireWorkerCommand(ownerId, memberId));

            result.Status.Should().Be("Success");
            result.BlockedVoucherCount.Should().Be(0);
        }

        await using var verify = CreateContext();
        (await verify.CompanyMembers.AnyAsync(m => m.Id == memberId)).Should().BeFalse();
    }

    [Fact]
    public async Task GiftThenRecall_RoundTrips_Durably()
    {
        var ownerId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var voucher1 = Guid.NewGuid();
        var voucher2 = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerId));
            seed.LegalEntities.Add(NewLegalEntity(legalEntityId, ownerId));
            seed.CompanyMembers.Add(NewMember(memberId, legalEntityId, workerId));
            // Pool vouchers: owner-held, no worker yet.
            var orderId = SeedPurchaseOrder(seed, ownerId);
            seed.FuelVouchers.AddRange(
                NewVoucher(voucher1, VoucherStatus.Assigned, legalEntityId, ownerId, workerUserId: null, orderId),
                NewVoucher(voucher2, VoucherStatus.Assigned, legalEntityId, ownerId, workerUserId: null, orderId));
            await seed.SaveChangesAsync();
        }

        await using (var giftCtx = CreateContext())
        {
            var gift = new GiftVouchersCommandHandler(giftCtx, new FuelFlowMetrics());
            var result = await gift.HandleAsync(new GiftVouchersCommand(ownerId, workerId, new[] { voucher1, voucher2 }));

            result.Status.Should().Be("Success");
            result.GiftedCount.Should().Be(2);
        }

        // Fresh context — the NoTracking Update() write path must have persisted both assignments.
        await using (var afterGift = CreateContext())
        {
            var gifted = await afterGift.FuelVouchers
                .Where(v => v.Id == voucher1 || v.Id == voucher2)
                .ToListAsync();
            gifted.Should().OnlyContain(v => v.WorkerUserId == workerId && v.Status == VoucherStatus.Assigned);
        }

        await using (var recallCtx = CreateContext())
        {
            var recall = new RecallVoucherCommandHandler(recallCtx, new FuelFlowMetrics());
            var result = await recall.HandleAsync(new RecallVoucherCommand(ownerId, voucher1));

            result.Status.Should().Be("Success");
        }

        await using var verify = CreateContext();
        var v1 = await verify.FuelVouchers.SingleAsync(v => v.Id == voucher1);
        var v2 = await verify.FuelVouchers.SingleAsync(v => v.Id == voucher2);
        v1.WorkerUserId.Should().BeNull("the recalled voucher returns to the pool");
        v1.Status.Should().Be(VoucherStatus.Assigned);
        v2.WorkerUserId.Should().Be(workerId, "the un-recalled voucher stays gifted");
    }

    [Fact]
    public async Task LegalEntities_RejectDuplicateEdrpou_AcrossUsers()
    {
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        await using var context = CreateContext();
        await ResetAsync(context);
        context.Users.AddRange(NewUser(user1), NewUser(user2));
        context.LegalEntities.Add(NewLegalEntity(Guid.NewGuid(), user1, edrpou: "12345678"));
        await context.SaveChangesAsync();

        // A different owner, the SAME EDRPOU — violates the unique index that InMemory silently ignores.
        context.LegalEntities.Add(NewLegalEntity(Guid.NewGuid(), user2, edrpou: "12345678"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task GetOwnerInvitations_ReturnsScopedInvitations_Durably()
    {
        // Regression for the GET /api/company/invitations 500: the handler ordered by
        // CreatedAtUtc over an already-projected DTO (built with the converted-enum ToString),
        // which Npgsql cannot translate and throws at query time. InMemory client-evaluated it,
        // so unit tests passed while prod returned 500. This exercises the real translation.
        var ownerId = Guid.NewGuid();
        var workerA = Guid.NewGuid();
        var workerB = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var otherEntityId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerA), NewUser(workerB));
            seed.LegalEntities.AddRange(
                NewLegalEntity(legalEntityId, ownerId, edrpou: "11111111"),
                NewLegalEntity(otherEntityId, ownerId, name: "OTHER LLC", edrpou: "22222222"));
            seed.CompanyInvitations.AddRange(
                NewInvitation(Guid.NewGuid(), legalEntityId, ownerId, workerA, InvitationStatus.Pending, "+380000000001", DateTime.UtcNow.AddMinutes(-10)),
                NewInvitation(Guid.NewGuid(), legalEntityId, ownerId, workerB, InvitationStatus.Accepted, "+380000000002", DateTime.UtcNow.AddMinutes(-5)),
                NewInvitation(Guid.NewGuid(), otherEntityId, ownerId, workerA, InvitationStatus.Pending, "+380000000003", DateTime.UtcNow));
            await seed.SaveChangesAsync();
        }

        await using var act = CreateContext();
        var handler = new GetOwnerInvitationsQueryHandler(act);

        // Scoped to one entity: returns only its two invitations, newest first, with the enum
        // stringified and the joined user names present.
        var scoped = await handler.HandleAsync(new GetOwnerInvitationsQuery(ownerId, legalEntityId));
        scoped.Should().HaveCount(2);
        scoped.Select(x => x.Status).Should().Equal("Accepted", "Pending");
        scoped.Should().OnlyContain(x => x.LegalEntityId == legalEntityId);

        // An entity the caller does not own: nothing to show.
        var foreign = await handler.HandleAsync(new GetOwnerInvitationsQuery(ownerId, Guid.NewGuid()));
        foreign.Should().BeEmpty();

        // Unscoped (back-compat): every invitation this owner sent, across entities.
        var all = await handler.HandleAsync(new GetOwnerInvitationsQuery(ownerId));
        all.Should().HaveCount(3);
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

    private static CompanyInvitation NewInvitation(
        Guid id,
        Guid legalEntityId,
        Guid ownerUserId,
        Guid workerUserId,
        InvitationStatus status,
        string workerPhoneNumber,
        DateTime createdAtUtc)
        => new()
        {
            Id = id,
            LegalEntityId = legalEntityId,
            OwnerUserId = ownerUserId,
            WorkerUserId = workerUserId,
            WorkerPhoneNumber = workerPhoneNumber,
            Status = status,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
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
    /// (<c>ck_voucher_held_has_order</c>); one order may well own a whole batch of vouchers.
    /// </summary>
    /// <summary>
    /// The point of issuance orders: one gift action produces one real order that owns the fuel, so
    /// the worker's wallet can show a receipt instead of a flat list, and the company can still trace
    /// what it bought. Asserted against real Postgres because the CHECK that forbids an orderless
    /// held voucher only exists there.
    /// </summary>
    [Fact]
    public async Task Gift_CreatesOneIssuanceOrder_ThatOwnsEveryGiftedVoucher()
    {
        var ownerId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var voucher1 = Guid.NewGuid();
        var voucher2 = Guid.NewGuid();

        Guid purchaseOrderId;

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerId));
            seed.LegalEntities.Add(NewLegalEntity(legalEntityId, ownerId));
            seed.CompanyMembers.Add(NewMember(memberId, legalEntityId, workerId));

            // A real purchase with a line item, so the issuance can carry the company's own cost.
            purchaseOrderId = SeedPurchaseOrder(seed, ownerId);
            seed.OrderLineItems.Add(new OrderLineItem
            {
                Id = Guid.NewGuid(),
                OrderId = purchaseOrderId,
                Provider = "OKKO",
                FuelTypeId = "okko-95",
                Liters = 50m,
                Quantity = 2,
                UnitPrice = 2500,
                LineTotal = 5000
            });

            seed.FuelVouchers.AddRange(
                NewVoucher(voucher1, VoucherStatus.Assigned, legalEntityId, ownerId, workerUserId: null, purchaseOrderId),
                NewVoucher(voucher2, VoucherStatus.Assigned, legalEntityId, ownerId, workerUserId: null, purchaseOrderId));
            await seed.SaveChangesAsync();
        }

        Guid? issuanceOrderId;

        await using (var giftCtx = CreateContext())
        {
            var gift = new GiftVouchersCommandHandler(giftCtx, new FuelFlowMetrics());
            var result = await gift.HandleAsync(new GiftVouchersCommand(ownerId, workerId, new[] { voucher1, voucher2 }));

            result.Status.Should().Be("Success");
            result.GiftedCount.Should().Be(2);
            issuanceOrderId = result.IssuanceOrderId;
        }

        issuanceOrderId.Should().NotBeNull();

        await using (var verify = CreateContext())
        {
            var issuance = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == issuanceOrderId!.Value);

            issuance.Kind.Should().Be(OrderKind.ReceivedFromCompany);
            issuance.Price.Should().Be(0);                 // a handover moves no money
            issuance.LegalEntityId.Should().Be(legalEntityId);
            issuance.UserId.Should().Be(workerId);         // the worker owns the receipt
            issuance.SourceOrderId.Should().Be(purchaseOrderId);
            issuance.Status.Should().Be(OrderStatus.Fulfilled);

            // The company's own price, carried for information only - never revenue.
            var lines = await verify.OrderLineItems.AsNoTracking()
                .Where(li => li.OrderId == issuanceOrderId!.Value).ToListAsync();
            lines.Should().HaveCount(2);
            lines.Should().OnlyContain(li => li.UnitPrice == 2500 && li.LineTotal == 2500);

            var fulfillments = await verify.Fulfillments.AsNoTracking()
                .Where(f => f.OrderId == issuanceOrderId!.Value).ToListAsync();
            fulfillments.Select(f => f.VoucherId).Should().BeEquivalentTo(new[] { voucher1, voucher2 });

            var gifted = await verify.FuelVouchers.AsNoTracking()
                .Where(v => v.Id == voucher1 || v.Id == voucher2).ToListAsync();

            gifted.Should().OnlyContain(v =>
                v.WorkerUserId == workerId
                && v.OrderId == issuanceOrderId!.Value
                && v.Status == VoucherStatus.Assigned);
        }
    }

    [Fact]
    public async Task Recall_PutsTheVoucherBackUnderThePurchase_NotTheIssuance()
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
            seed.FuelVouchers.Add(NewVoucher(voucherId, VoucherStatus.Assigned, legalEntityId, ownerId, workerUserId: null, purchaseOrderId));
            await seed.SaveChangesAsync();
        }

        await using (var giftCtx = CreateContext())
        {
            await new GiftVouchersCommandHandler(giftCtx, new FuelFlowMetrics())
                .HandleAsync(new GiftVouchersCommand(ownerId, workerId, new[] { voucherId }));
        }

        await using (var recallCtx = CreateContext())
        {
            var result = await new RecallVoucherCommandHandler(recallCtx, new FuelFlowMetrics())
                .HandleAsync(new RecallVoucherCommand(ownerId, voucherId));

            result.Status.Should().Be("Success");
        }

        await using var verify = CreateContext();
        var recalled = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucherId);

        // Back in the pool: no worker, and the fuel belongs to the purchase again rather than to a
        // handover that no longer describes it.
        recalled.WorkerUserId.Should().BeNull();
        recalled.Status.Should().Be(VoucherStatus.Assigned);
        recalled.OrderId.Should().Be(purchaseOrderId);
    }

    [Fact]
    public async Task Firing_ReturnsStillAssignedFuelToThePurchase_ButAFrozenVoucherKeepsItsIssuance()
    {
        var ownerId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var liveVoucher = Guid.NewGuid();
        var frozenVoucher = Guid.NewGuid();

        Guid purchaseOrderId;

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.Users.AddRange(NewUser(ownerId), NewUser(workerId));
            seed.LegalEntities.Add(NewLegalEntity(legalEntityId, ownerId));
            seed.CompanyMembers.Add(NewMember(memberId, legalEntityId, workerId));
            purchaseOrderId = SeedPurchaseOrder(seed, ownerId);
            seed.FuelVouchers.AddRange(
                NewVoucher(liveVoucher, VoucherStatus.Assigned, legalEntityId, ownerId, workerUserId: null, purchaseOrderId),
                NewVoucher(frozenVoucher, VoucherStatus.Assigned, legalEntityId, ownerId, workerUserId: null, purchaseOrderId));
            await seed.SaveChangesAsync();
        }

        await using (var giftCtx = CreateContext())
        {
            await new GiftVouchersCommandHandler(giftCtx, new FuelFlowMetrics())
                .HandleAsync(new GiftVouchersCommand(ownerId, workerId, new[] { liveVoucher, frozenVoucher }));
        }

        await using (var freezeCtx = CreateContext())
        {
            var frozen = await new BlockWorkerVoucherCommandHandler(freezeCtx, new FuelFlowMetrics())
                .HandleAsync(new BlockWorkerVoucherCommand(ownerId, frozenVoucher, legalEntityId));

            frozen.Status.Should().Be("Success");
        }

        await using (var fireCtx = CreateContext())
        {
            var result = await new FireWorkerCommandHandler(fireCtx, new FuelFlowMetrics())
                .HandleAsync(new FireWorkerCommand(ownerId, memberId));

            result.Status.Should().Be("Success");
            result.BlockedVoucherCount.Should().Be(1);
        }

        await using var verify = CreateContext();

        // Fuel that leaves with the worker is company fuel again.
        var live = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == liveVoucher);
        live.Status.Should().Be(VoucherStatus.Blocked);
        live.WorkerUserId.Should().BeNull();
        live.OrderId.Should().Be(purchaseOrderId);

        // A frozen voucher is still the worker's, so it keeps the handover and the worker can see
        // why it is unusable. Firing only touches vouchers still Assigned.
        var frozenVoucherRow = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == frozenVoucher);
        frozenVoucherRow.Status.Should().Be(VoucherStatus.Blocked);
        frozenVoucherRow.WorkerUserId.Should().Be(workerId);
        frozenVoucherRow.OrderId.Should().NotBe(purchaseOrderId);
    }

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
            """TRUNCATE TABLE "refunds", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "company_invitations", "company_members", "legal_entities", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}
