using FluentAssertions;
using FuelFlow.Features.Company.FireWorker;
using FuelFlow.Features.Company.GiftVouchers;
using FuelFlow.Features.Company.RecallVoucher;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Contracts.SharedModels;
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
            seed.FuelVouchers.AddRange(
                NewVoucher(gifted1, VoucherStatus.Assigned, legalEntityId, ownerId, workerId),
                NewVoucher(gifted2, VoucherStatus.Assigned, legalEntityId, ownerId, workerId),
                NewVoucher(usedVoucher, VoucherStatus.Used, legalEntityId, ownerId, workerId),
                NewVoucher(otherWorkerVoucher, VoucherStatus.Assigned, legalEntityId, ownerId, otherWorkerId));
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
            seed.FuelVouchers.AddRange(
                NewVoucher(voucher1, VoucherStatus.Assigned, legalEntityId, ownerId, workerUserId: null),
                NewVoucher(voucher2, VoucherStatus.Assigned, legalEntityId, ownerId, workerUserId: null));
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
        Guid? workerUserId = null)
        => new()
        {
            Id = id,
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50m,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = $"OKKO-{id:N}"[..20],
            QrPayload = $"payload-{id:N}",
            Status = status,
            LegalEntityId = legalEntityId,
            AssignedToUserId = assignedToUserId,
            WorkerUserId = workerUserId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static async Task ResetAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "refunds", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "company_invitations", "company_members", "legal_entities", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}
