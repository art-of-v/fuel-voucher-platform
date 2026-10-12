using FluentAssertions;
using FuelFlow.Features.Company.AcceptInvitation;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Persistence;

namespace FuelFlow.UnitTests.Company;

public sealed class CompanyAcceptInvitationCommandHandlerTests : IDisposable
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();

    private readonly ApplicationDbContext _context = CompanyTestFactory.CreateContext();
    private readonly AcceptInvitationCommandHandler _handler;

    public CompanyAcceptInvitationCommandHandlerTests()
    {
        _handler = new AcceptInvitationCommandHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private CompanyInvitation AddInvitation(InvitationStatus status = InvitationStatus.Pending, Guid? workerId = null)
    {
        var inv = CompanyTestFactory.NewInvitation(
            Guid.NewGuid(), LegalEntityId, OwnerId, workerId ?? WorkerId, "+380000000002", status);
        _context.CompanyInvitations.Add(inv);
        return inv;
    }

    [Fact]
    public async Task Accept_ReturnsNotFound_WhenInvitationMissing()
    {
        var result = await _handler.HandleAsync(new AcceptInvitationCommand(Guid.NewGuid(), WorkerId));
        result.Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task Accept_ReturnsNotFound_WhenInvitationBelongsToAnotherWorker()
    {
        var inv = AddInvitation(workerId: Guid.NewGuid());
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new AcceptInvitationCommand(inv.Id, WorkerId));
        result.Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task Accept_ReturnsInvalidStatus_WhenNotPending()
    {
        var inv = AddInvitation(InvitationStatus.Cancelled);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new AcceptInvitationCommand(inv.Id, WorkerId));
        result.Status.Should().Be("InvalidStatus");
    }

[Fact]
       public async Task Accept_ReturnsAlreadyMember_WhenWorkerAlreadyInThisCompany()
       {
           var inv = AddInvitation();
           // Same company as the invitation. This used to seed a *different* legal entity, which
           // only passed because the guard was global - #150 W1.
           _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), LegalEntityId, WorkerId));
           await _context.SaveChangesAsync();

           var result = await _handler.HandleAsync(new AcceptInvitationCommand(inv.Id, WorkerId));
           result.Status.Should().Be("AlreadyMember");
       }

       /// <summary>
       /// The point of W1: working for N companies. A membership elsewhere must not block this
       /// invitation - under the old global check it refused every second company.
       /// </summary>
       [Fact]
       public async Task Accept_Succeeds_WhenWorkerAlreadyWorksForAnotherCompany()
       {
           var inv = AddInvitation();
           _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), Guid.NewGuid(), WorkerId));
           await _context.SaveChangesAsync();

           var result = await _handler.HandleAsync(new AcceptInvitationCommand(inv.Id, WorkerId));

           result.Status.Should().Be("Success");
           var member = await _context.CompanyMembers.FindAsync(result.MemberId!.Value);
           member!.LegalEntityId.Should().Be(LegalEntityId);
           member.WorkerUserId.Should().Be(WorkerId);
       }

    [Fact]
    public async Task Accept_CreatesMemberAndMarksAccepted_OnSuccess()
    {
        var inv = AddInvitation();
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new AcceptInvitationCommand(inv.Id, WorkerId));

        result.Status.Should().Be("Success");
        result.MemberId.Should().NotBeNull();
        var member = await _context.CompanyMembers.FindAsync(result.MemberId!.Value);
        member!.LegalEntityId.Should().Be(LegalEntityId);
        member.WorkerUserId.Should().Be(WorkerId);
        (await _context.CompanyInvitations.FindAsync(inv.Id))!.Status.Should().Be(InvitationStatus.Accepted);
    }
}
