using FluentAssertions;
using FuelFlow.Features.Company.GetMyMemberships;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Persistence;

namespace FuelFlow.UnitTests.Company;

/// <summary>
/// The worker-context feed (epic #103 S5). It must list exactly the companies the
/// caller is a member of — never a company they only have a pending invitation for,
/// and never somebody else's company — and flag the case where the caller owns the
/// company they work for, so the client can show one row with owner rights.
/// </summary>
public sealed class CompanyGetMyMembershipsQueryHandlerTests : IDisposable
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid OtherWorkerId = Guid.NewGuid();
    private static readonly Guid AcmeId = Guid.NewGuid();
    private static readonly Guid GlobexId = Guid.NewGuid();

    private readonly ApplicationDbContext _context = CompanyTestFactory.CreateContext();

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task SeedAsync()
    {
        _context.Users.Add(CompanyTestFactory.NewUser(OwnerId, "+380000000001", "Olga", "Owner"));
        _context.Users.Add(CompanyTestFactory.NewUser(WorkerId, "+380000000002", "Ivan", "Worker"));
        _context.Users.Add(CompanyTestFactory.NewUser(OtherWorkerId, "+380000000003", "Petro", "Other"));
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(AcmeId, OwnerId, "ACME LLC", "11111111"));
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(GlobexId, OwnerId, "Globex LLC", "22222222"));
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), AcmeId, WorkerId));
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), GlobexId, OtherWorkerId));
        // A pending invitation is not a membership — it must not create a context.
        _context.CompanyInvitations.Add(CompanyTestFactory.NewInvitation(
            Guid.NewGuid(), GlobexId, OwnerId, WorkerId, "+380000000002", InvitationStatus.Pending));
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_ReturnsOnlyCompaniesTheCallerIsAMemberOf()
    {
        await SeedAsync();

        var handler = new GetMyMembershipsQueryHandler(_context);
        var result = await handler.HandleAsync(new GetMyMembershipsQuery(WorkerId));

        result.Should().ContainSingle();
        result[0].LegalEntityId.Should().Be(AcmeId);
        result[0].Name.Should().Be("ACME LLC");
        result[0].Edrpou.Should().Be("11111111");
        result[0].OwnerUserId.Should().Be(OwnerId);
        result[0].IsOwner.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ReturnsEmpty_ForAUserWithNoMembership()
    {
        await SeedAsync();

        var handler = new GetMyMembershipsQueryHandler(_context);
        var result = await handler.HandleAsync(new GetMyMembershipsQuery(Guid.NewGuid()));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_FlagsIsOwner_WhenTheCallerOwnsTheCompanyTheyWorkFor()
    {
        await SeedAsync();
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), AcmeId, OwnerId));
        await _context.SaveChangesAsync();

        var handler = new GetMyMembershipsQueryHandler(_context);
        var result = await handler.HandleAsync(new GetMyMembershipsQuery(OwnerId));

        result.Should().ContainSingle();
        result[0].IsOwner.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ListsEveryMembership_SoSeveralCompaniesCanBeWorkedFor()
    {
        await SeedAsync();
        var thirdId = Guid.NewGuid();
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(thirdId, Guid.NewGuid(), "Initech LLC", "33333333"));
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), thirdId, WorkerId));
        await _context.SaveChangesAsync();

        var handler = new GetMyMembershipsQueryHandler(_context);
        var result = await handler.HandleAsync(new GetMyMembershipsQuery(WorkerId));

        // Ordered by name so the switcher stays stable across refetches.
        result.Select(x => x.Name).Should().ContainInOrder("ACME LLC", "Initech LLC");
    }
}