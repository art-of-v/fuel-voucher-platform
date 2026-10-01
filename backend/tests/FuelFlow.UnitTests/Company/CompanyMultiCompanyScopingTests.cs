using FluentAssertions;
using FuelFlow.Features.Company.FireWorker;
using FuelFlow.Features.Company.GetMembers;
using FuelFlow.Features.Company.GetOwnerInvitations;
using FuelFlow.Features.Company.GiftVouchers;
using FuelFlow.Features.Company.RecallVoucher;
using FuelFlow.Features.Company.SendInvitation;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Observability;
using Moq;

namespace FuelFlow.UnitTests.Company;

/// <summary>
/// Epic #103 S3a: company owner endpoints must scope to an explicitly selected legal entity.
/// Before S3a every handler resolved the owner's FIRST entity, so companies 2..N were
/// unmanageable (S0 dropped the one-entity-per-user constraint). These tests pin the three
/// branches of <c>OwnerCompanyResolver</c> through the real handlers:
///   • an explicit OWNED id targets THAT entity (not the first),
///   • an explicit UNOWNED id is rejected (CompanyNotOwned / empty for queries),
///   • an omitted id falls back to the owner's OLDEST entity (back-compat for shipped clients).
/// Phone numbers are fully synthetic.
/// </summary>
public sealed class CompanyMultiCompanyScopingTests : IDisposable
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid FirstEntityId = Guid.NewGuid();
    private static readonly Guid SecondEntityId = Guid.NewGuid();
    private static readonly Guid StrangerEntityId = Guid.NewGuid();

    private readonly ApplicationDbContext _context = CompanyTestFactory.CreateContext();

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    /// <summary>Owner holds two companies (first older than second); a stranger owns a third.</summary>
    private async Task SeedTwoOwnedCompaniesAsync()
    {
        var first = CompanyTestFactory.NewLegalEntity(FirstEntityId, OwnerId, name: "First LLC", edrpou: "10000001");
        first.CreatedAtUtc = DateTime.UtcNow.AddDays(-10);
        var second = CompanyTestFactory.NewLegalEntity(SecondEntityId, OwnerId, name: "Second LLC", edrpou: "10000002");
        second.CreatedAtUtc = DateTime.UtcNow.AddDays(-1);
        var stranger = CompanyTestFactory.NewLegalEntity(StrangerEntityId, Guid.NewGuid(), name: "Stranger LLC", edrpou: "20000003");

        _context.LegalEntities.AddRange(first, second, stranger);
        await _context.SaveChangesAsync();
    }

    private static IPhoneNumberService IdentityPhone()
    {
        var mock = new Mock<IPhoneNumberService>();
        mock.Setup(x => x.Normalize(It.IsAny<string>())).Returns<string>(s => s);
        return mock.Object;
    }

    // ---- GetMembers (query) --------------------------------------------------

    [Fact]
    public async Task GetMembers_ScopesToExplicitlySelectedEntity_NotTheFirst()
    {
        await SeedTwoOwnedCompaniesAsync();
        var firstWorker = Guid.NewGuid();
        var secondWorker = Guid.NewGuid();
        _context.Users.Add(CompanyTestFactory.NewUser(firstWorker, "+380000000011", "First", "Worker"));
        _context.Users.Add(CompanyTestFactory.NewUser(secondWorker, "+380000000012", "Second", "Worker"));
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), FirstEntityId, firstWorker));
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), SecondEntityId, secondWorker));
        await _context.SaveChangesAsync();

        var result = await new GetMembersQueryHandler(_context)
            .HandleAsync(new GetMembersQuery(OwnerId, SecondEntityId));

        result.Should().ContainSingle();
        result[0].WorkerUserId.Should().Be(secondWorker);
    }

    [Fact]
    public async Task GetMembers_FallsBackToOldestEntity_WhenIdOmitted()
    {
        await SeedTwoOwnedCompaniesAsync();
        var firstWorker = Guid.NewGuid();
        _context.Users.Add(CompanyTestFactory.NewUser(firstWorker, "+380000000011"));
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), FirstEntityId, firstWorker));
        await _context.SaveChangesAsync();

        var result = await new GetMembersQueryHandler(_context)
            .HandleAsync(new GetMembersQuery(OwnerId));

        result.Should().ContainSingle();
        result[0].WorkerUserId.Should().Be(firstWorker);
    }

    [Fact]
    public async Task GetMembers_ReturnsEmpty_WhenEntityNotOwned()
    {
        await SeedTwoOwnedCompaniesAsync();

        var result = await new GetMembersQueryHandler(_context)
            .HandleAsync(new GetMembersQuery(OwnerId, StrangerEntityId));

        result.Should().BeEmpty();
    }

    // ---- GetOwnerInvitations (query) ----------------------------------------

    [Fact]
    public async Task GetOwnerInvitations_FiltersToSelectedEntity()
    {
        await SeedTwoOwnedCompaniesAsync();
        var w1 = Guid.NewGuid();
        var w2 = Guid.NewGuid();
        _context.Users.Add(CompanyTestFactory.NewUser(w1, "+380000000011"));
        _context.Users.Add(CompanyTestFactory.NewUser(w2, "+380000000012"));
        _context.CompanyInvitations.Add(CompanyTestFactory.NewInvitation(Guid.NewGuid(), FirstEntityId, OwnerId, w1, "+380000000011"));
        _context.CompanyInvitations.Add(CompanyTestFactory.NewInvitation(Guid.NewGuid(), SecondEntityId, OwnerId, w2, "+380000000012"));
        await _context.SaveChangesAsync();

        var result = await new GetOwnerInvitationsQueryHandler(_context)
            .HandleAsync(new GetOwnerInvitationsQuery(OwnerId, SecondEntityId));

        result.Should().ContainSingle();
        result[0].LegalEntityId.Should().Be(SecondEntityId);
    }

    [Fact]
    public async Task GetOwnerInvitations_ReturnsEmpty_WhenEntityNotOwned()
    {
        await SeedTwoOwnedCompaniesAsync();
        var w1 = Guid.NewGuid();
        _context.Users.Add(CompanyTestFactory.NewUser(w1, "+380000000011"));
        _context.CompanyInvitations.Add(CompanyTestFactory.NewInvitation(Guid.NewGuid(), FirstEntityId, OwnerId, w1, "+380000000011"));
        await _context.SaveChangesAsync();

        var result = await new GetOwnerInvitationsQueryHandler(_context)
            .HandleAsync(new GetOwnerInvitationsQuery(OwnerId, StrangerEntityId));

        result.Should().BeEmpty();
    }

    // ---- SendInvitation (mutation) ------------------------------------------

    [Fact]
    public async Task SendInvitation_ReturnsCompanyNotOwned_WhenEntityNotOwned()
    {
        await SeedTwoOwnedCompaniesAsync();
        _context.Users.Add(CompanyTestFactory.NewUser(OwnerId, "+380000000001"));
        _context.Users.Add(CompanyTestFactory.NewUser(Guid.NewGuid(), "+380000000002"));
        await _context.SaveChangesAsync();

        var result = await new SendInvitationCommandHandler(_context, IdentityPhone())
            .HandleAsync(new SendInvitationCommand(OwnerId, "+380000000002", StrangerEntityId));

        result.Status.Should().Be("CompanyNotOwned");
    }

    [Fact]
    public async Task SendInvitation_CreatesInvitationOnSelectedEntity()
    {
        await SeedTwoOwnedCompaniesAsync();
        var workerId = Guid.NewGuid();
        _context.Users.Add(CompanyTestFactory.NewUser(OwnerId, "+380000000001"));
        _context.Users.Add(CompanyTestFactory.NewUser(workerId, "+380000000002"));
        await _context.SaveChangesAsync();

        var result = await new SendInvitationCommandHandler(_context, IdentityPhone())
            .HandleAsync(new SendInvitationCommand(OwnerId, "+380000000002", SecondEntityId));

        result.Status.Should().Be("Success");
        var invitation = _context.CompanyInvitations.Single();
        invitation.LegalEntityId.Should().Be(SecondEntityId);
    }

    // ---- GiftVouchers (mutation) --------------------------------------------

    [Fact]
    public async Task GiftVouchers_ReturnsCompanyNotOwned_WhenEntityNotOwned()
    {
        await SeedTwoOwnedCompaniesAsync();

        var result = await new GiftVouchersCommandHandler(_context, new FuelFlowMetrics())
            .HandleAsync(new GiftVouchersCommand(OwnerId, Guid.NewGuid(), new[] { Guid.NewGuid() }, StrangerEntityId));

        result.Status.Should().Be("CompanyNotOwned");
    }

    [Fact]
    public async Task GiftVouchers_GiftsFromSelectedEntityPool()
    {
        await SeedTwoOwnedCompaniesAsync();
        var workerId = Guid.NewGuid();
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), SecondEntityId, workerId));
        var voucher = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: SecondEntityId, assignedToUserId: OwnerId, workerUserId: null);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var result = await new GiftVouchersCommandHandler(_context, new FuelFlowMetrics())
            .HandleAsync(new GiftVouchersCommand(OwnerId, workerId, new[] { voucher.Id }, SecondEntityId));

        result.Status.Should().Be("Success");
        _context.FuelVouchers.Single(x => x.Id == voucher.Id).WorkerUserId.Should().Be(workerId);
    }

    // ---- FireWorker (mutation) ----------------------------------------------

    [Fact]
    public async Task FireWorker_ReturnsCompanyNotOwned_WhenEntityNotOwned()
    {
        await SeedTwoOwnedCompaniesAsync();

        var result = await new FireWorkerCommandHandler(_context, new FuelFlowMetrics())
            .HandleAsync(new FireWorkerCommand(OwnerId, Guid.NewGuid(), StrangerEntityId));

        result.Status.Should().Be("CompanyNotOwned");
    }

    [Fact]
    public async Task FireWorker_RemovesMemberFromSelectedEntity()
    {
        await SeedTwoOwnedCompaniesAsync();
        var workerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(memberId, SecondEntityId, workerId));
        await _context.SaveChangesAsync();

        var result = await new FireWorkerCommandHandler(_context, new FuelFlowMetrics())
            .HandleAsync(new FireWorkerCommand(OwnerId, memberId, SecondEntityId));

        result.Status.Should().Be("Success");
        _context.CompanyMembers.Any(x => x.Id == memberId).Should().BeFalse();
    }

    [Fact]
    public async Task FireWorker_ReturnsNotFound_WhenMemberBelongsToADifferentOwnedEntity()
    {
        await SeedTwoOwnedCompaniesAsync();
        var workerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        // Member lives under the FIRST entity, but the caller selected the SECOND.
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(memberId, FirstEntityId, workerId));
        await _context.SaveChangesAsync();

        var result = await new FireWorkerCommandHandler(_context, new FuelFlowMetrics())
            .HandleAsync(new FireWorkerCommand(OwnerId, memberId, SecondEntityId));

        result.Status.Should().Be("NotFound");
    }

    // ---- RecallVoucher (mutation) -------------------------------------------

    [Fact]
    public async Task RecallVoucher_ReturnsCompanyNotOwned_WhenEntityNotOwned()
    {
        await SeedTwoOwnedCompaniesAsync();

        var result = await new RecallVoucherCommandHandler(_context, new FuelFlowMetrics())
            .HandleAsync(new RecallVoucherCommand(OwnerId, Guid.NewGuid(), StrangerEntityId));

        result.Status.Should().Be("CompanyNotOwned");
    }

    [Fact]
    public async Task RecallVoucher_RecallsFromSelectedEntity()
    {
        await SeedTwoOwnedCompaniesAsync();
        var workerId = Guid.NewGuid();
        var voucher = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: SecondEntityId, assignedToUserId: OwnerId, workerUserId: workerId);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var result = await new RecallVoucherCommandHandler(_context, new FuelFlowMetrics())
            .HandleAsync(new RecallVoucherCommand(OwnerId, voucher.Id, SecondEntityId));

        result.Status.Should().Be("Success");
        _context.FuelVouchers.Single(x => x.Id == voucher.Id).WorkerUserId.Should().BeNull();
    }
}
