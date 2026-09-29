using FluentAssertions;
using FuelFlow.Features.Company.CancelInvitation;
using FuelFlow.Features.Company.DeclineInvitation;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Persistence;

namespace FuelFlow.UnitTests.Company;

public sealed class CompanyInvitationLifecycleCommandHandlersTests : IDisposable
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

    private CompanyInvitation AddInvitation(InvitationStatus status = InvitationStatus.Pending)
    {
        var inv = CompanyTestFactory.NewInvitation(
            Guid.NewGuid(), LegalEntityId, OwnerId, WorkerId, "+380000000002", status);
        _context.CompanyInvitations.Add(inv);
        return inv;
    }

    // ---- Decline (worker action) ----

    [Fact]
    public async Task Decline_ReturnsNotFound_WhenInvitationBelongsToAnotherWorker()
    {
        var inv = AddInvitation();
        await _context.SaveChangesAsync();

        var handler = new DeclineInvitationCommandHandler(_context);
        var result = await handler.HandleAsync(new DeclineInvitationCommand(inv.Id, Guid.NewGuid()));
        result.Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task Decline_ReturnsInvalidStatus_WhenNotPending()
    {
        var inv = AddInvitation(InvitationStatus.Accepted);
        await _context.SaveChangesAsync();

        var handler = new DeclineInvitationCommandHandler(_context);
        var result = await handler.HandleAsync(new DeclineInvitationCommand(inv.Id, WorkerId));
        result.Status.Should().Be("InvalidStatus");
    }

    [Fact]
    public async Task Decline_MarksDeclined_OnSuccess()
    {
        var inv = AddInvitation();
        await _context.SaveChangesAsync();

        var handler = new DeclineInvitationCommandHandler(_context);
        var result = await handler.HandleAsync(new DeclineInvitationCommand(inv.Id, WorkerId));

        result.Status.Should().Be("Success");
        (await _context.CompanyInvitations.FindAsync(inv.Id))!.Status.Should().Be(InvitationStatus.Declined);
    }

    // ---- Cancel (owner action) ----

    [Fact]
    public async Task Cancel_ReturnsNotFound_WhenInvitationBelongsToAnotherOwner()
    {
        var inv = AddInvitation();
        await _context.SaveChangesAsync();

        var handler = new CancelInvitationCommandHandler(_context);
        var result = await handler.HandleAsync(new CancelInvitationCommand(inv.Id, Guid.NewGuid()));
        result.Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task Cancel_ReturnsInvalidStatus_WhenNotPending()
    {
        var inv = AddInvitation(InvitationStatus.Declined);
        await _context.SaveChangesAsync();

        var handler = new CancelInvitationCommandHandler(_context);
        var result = await handler.HandleAsync(new CancelInvitationCommand(inv.Id, OwnerId));
        result.Status.Should().Be("InvalidStatus");
    }

    [Fact]
    public async Task Cancel_MarksCancelled_OnSuccess()
    {
        var inv = AddInvitation();
        await _context.SaveChangesAsync();

        var handler = new CancelInvitationCommandHandler(_context);
        var result = await handler.HandleAsync(new CancelInvitationCommand(inv.Id, OwnerId));

        result.Status.Should().Be("Success");
        (await _context.CompanyInvitations.FindAsync(inv.Id))!.Status.Should().Be(InvitationStatus.Cancelled);
    }
}
