using FluentAssertions;
using FuelFlow.Features.Company.GetMyInvitations;
using FuelFlow.Features.Company.GetOwnerInvitations;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Persistence;

namespace FuelFlow.UnitTests.Company;

public sealed class CompanyGetInvitationsQueryHandlersTests : IDisposable
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();

    private readonly ApplicationDbContext _context = CompanyTestFactory.CreateContext();

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task SeedGraphAsync()
    {
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(LegalEntityId, OwnerId, "ACME LLC"));
        _context.Users.Add(CompanyTestFactory.NewUser(OwnerId, "+380000000001", "Olga", "Owner"));
        _context.Users.Add(CompanyTestFactory.NewUser(WorkerId, "+380000000002", "Ivan", "Worker"));
        await _context.SaveChangesAsync();
    }

    private CompanyInvitation AddInvitation(InvitationStatus status, Guid? ownerId = null, Guid? workerId = null)
    {
        var inv = CompanyTestFactory.NewInvitation(
            Guid.NewGuid(), LegalEntityId, ownerId ?? OwnerId, workerId ?? WorkerId, "+380000000002", status);
        _context.CompanyInvitations.Add(inv);
        return inv;
    }

    [Fact]
    public async Task GetMyInvitations_ReturnsOnlyPendingForThatWorker()
    {
        await SeedGraphAsync();
        var pending = AddInvitation(InvitationStatus.Pending);
        AddInvitation(InvitationStatus.Declined);
        AddInvitation(InvitationStatus.Pending, workerId: Guid.NewGuid());
        await _context.SaveChangesAsync();

        var handler = new GetMyInvitationsQueryHandler(_context);
        var result = await handler.HandleAsync(new GetMyInvitationsQuery(WorkerId));

        result.Should().ContainSingle();
        result[0].Id.Should().Be(pending.Id);
        result[0].LegalEntityName.Should().Be("ACME LLC");
        result[0].OwnerFirstName.Should().Be("Olga");
    }

    [Fact]
    public async Task GetOwnerInvitations_ReturnsAllStatusesForThatOwner()
    {
        await SeedGraphAsync();
        AddInvitation(InvitationStatus.Pending);
        AddInvitation(InvitationStatus.Declined);
        // A different owner's invitation must not leak into this owner's list.
        AddInvitation(InvitationStatus.Cancelled, ownerId: Guid.NewGuid());
        await _context.SaveChangesAsync();

        var handler = new GetOwnerInvitationsQueryHandler(_context);
        var result = await handler.HandleAsync(new GetOwnerInvitationsQuery(OwnerId));

        result.Should().HaveCount(2);
        result.Should().Contain(x => x.Status == "Pending");
        result.Should().Contain(x => x.Status == "Declined");
        result.Should().OnlyContain(x => x.WorkerFirstName == "Ivan");
    }

    [Fact]
    public async Task GetOwnerInvitations_ReturnsEmpty_ForOwnerWithNone()
    {
        await SeedGraphAsync();

        var handler = new GetOwnerInvitationsQueryHandler(_context);
        var result = await handler.HandleAsync(new GetOwnerInvitationsQuery(Guid.NewGuid()));
        result.Should().BeEmpty();
    }
}
