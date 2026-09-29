using FluentAssertions;
using FuelFlow.Features.Company.FireWorker;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;

namespace FuelFlow.UnitTests.Company;

public sealed class CompanyFireWorkerCommandHandlerTests : IDisposable
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();

    private readonly ApplicationDbContext _context = CompanyTestFactory.CreateContext();
    private readonly FireWorkerCommandHandler _handler;

    public CompanyFireWorkerCommandHandlerTests()
    {
        _handler = new FireWorkerCommandHandler(_context, new FuelFlowMetrics());
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task Fire_ReturnsOwnerCompanyNotFound_WhenOwnerHasNoLegalEntity()
    {
        var result = await _handler.HandleAsync(new FireWorkerCommand(OwnerId, Guid.NewGuid()));
        result.Status.Should().Be("OwnerCompanyNotFound");
    }

    [Fact]
    public async Task Fire_ReturnsNotFound_WhenMemberBelongsToAnotherCompany()
    {
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(LegalEntityId, OwnerId));
        // Member row exists, but scoped to a different legal entity — must not be reachable by this owner.
        var member = CompanyTestFactory.NewMember(Guid.NewGuid(), Guid.NewGuid(), WorkerId);
        _context.CompanyMembers.Add(member);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new FireWorkerCommand(OwnerId, member.Id));
        result.Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task Fire_BlocksAssignedWorkerVouchersAndRemovesMember_OnSuccess()
    {
        var otherWorkerId = Guid.NewGuid();
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(LegalEntityId, OwnerId));
        var member = CompanyTestFactory.NewMember(Guid.NewGuid(), LegalEntityId, WorkerId);
        _context.CompanyMembers.Add(member);

        var gifted1 = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: LegalEntityId, assignedToUserId: OwnerId, workerUserId: WorkerId);
        var gifted2 = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: LegalEntityId, assignedToUserId: OwnerId, workerUserId: WorkerId);
        var usedByWorker = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Used,
            legalEntityId: LegalEntityId, assignedToUserId: OwnerId, workerUserId: WorkerId);
        var otherWorkerVoucher = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: LegalEntityId, assignedToUserId: OwnerId, workerUserId: otherWorkerId);
        _context.FuelVouchers.AddRange(gifted1, gifted2, usedByWorker, otherWorkerVoucher);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new FireWorkerCommand(OwnerId, member.Id));

        result.Status.Should().Be("Success");
        result.BlockedVoucherCount.Should().Be(2);
        (await _context.CompanyMembers.FindAsync(member.Id)).Should().BeNull();

        var blocked = await _context.FuelVouchers.FindAsync(gifted1.Id);
        blocked!.Status.Should().Be(VoucherStatus.Blocked);
        blocked.WorkerUserId.Should().BeNull();

        // Historical (Used) vouchers must not be blocked.
        (await _context.FuelVouchers.FindAsync(usedByWorker.Id))!.Status.Should().Be(VoucherStatus.Used);
        // Another worker's active gift is untouched.
        var other = await _context.FuelVouchers.FindAsync(otherWorkerVoucher.Id);
        other!.Status.Should().Be(VoucherStatus.Assigned);
        other.WorkerUserId.Should().Be(otherWorkerId);
    }

    [Fact]
    public async Task Fire_RemovesMemberWithZeroCount_WhenNoAssignedVouchers()
    {
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(LegalEntityId, OwnerId));
        var member = CompanyTestFactory.NewMember(Guid.NewGuid(), LegalEntityId, WorkerId);
        _context.CompanyMembers.Add(member);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new FireWorkerCommand(OwnerId, member.Id));

        result.Status.Should().Be("Success");
        result.BlockedVoucherCount.Should().Be(0);
        (await _context.CompanyMembers.FindAsync(member.Id)).Should().BeNull();
    }
}
