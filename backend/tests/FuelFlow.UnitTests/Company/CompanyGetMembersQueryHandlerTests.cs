using FluentAssertions;
using FuelFlow.Features.Company.GetMembers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;

namespace FuelFlow.UnitTests.Company;

public sealed class CompanyGetMembersQueryHandlerTests : IDisposable
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();

    private readonly ApplicationDbContext _context = CompanyTestFactory.CreateContext();
    private readonly GetMembersQueryHandler _handler;

    public CompanyGetMembersQueryHandlerTests()
    {
        _handler = new GetMembersQueryHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task GetMembers_ReturnsEmpty_WhenOwnerHasNoLegalEntity()
    {
        var result = await _handler.HandleAsync(new GetMembersQuery(OwnerId));
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMembers_ReturnsOnlyOwnCompanyMembers()
    {
        var workerId = Guid.NewGuid();
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(LegalEntityId, OwnerId));
        _context.Users.Add(CompanyTestFactory.NewUser(workerId, "+380000000002", "Ivan", "Petrenko"));
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), LegalEntityId, workerId));

        // A member of a different company must never appear in this owner's roster.
        var otherWorkerId = Guid.NewGuid();
        _context.Users.Add(CompanyTestFactory.NewUser(otherWorkerId, "+380000000003"));
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), Guid.NewGuid(), otherWorkerId));
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new GetMembersQuery(OwnerId));

        result.Should().ContainSingle();
        result[0].WorkerUserId.Should().Be(workerId);
        result[0].WorkerFirstName.Should().Be("Ivan");
    }

    [Fact]
    public async Task GetMembers_CountsOnlyActiveGiftedVouchers()
    {
        var workerId = Guid.NewGuid();
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(LegalEntityId, OwnerId));
        _context.Users.Add(CompanyTestFactory.NewUser(workerId, "+380000000002"));
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), LegalEntityId, workerId));

        // 2 active gifts (Assigned + worker) → counted.
        _context.FuelVouchers.Add(CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: LegalEntityId, assignedToUserId: OwnerId, workerUserId: workerId));
        _context.FuelVouchers.Add(CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: LegalEntityId, assignedToUserId: OwnerId, workerUserId: workerId));
        // A used gift and a recalled (worker-less) pool voucher → excluded from the gifted count.
        _context.FuelVouchers.Add(CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Used,
            legalEntityId: LegalEntityId, assignedToUserId: OwnerId, workerUserId: workerId));
        _context.FuelVouchers.Add(CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: LegalEntityId, assignedToUserId: OwnerId, workerUserId: null));
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new GetMembersQuery(OwnerId));

        result.Should().ContainSingle();
        result[0].GiftedVoucherCount.Should().Be(2);
    }
}
