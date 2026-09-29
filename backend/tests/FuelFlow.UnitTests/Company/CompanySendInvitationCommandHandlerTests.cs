using FluentAssertions;
using FuelFlow.Features.Company.SendInvitation;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Abstractions;
using Moq;

namespace FuelFlow.UnitTests.Company;

public sealed class CompanySendInvitationCommandHandlerTests : IDisposable
{
    private const string WorkerPhone = "+380000000002";
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();

    private readonly ApplicationDbContext _context = CompanyTestFactory.CreateContext();

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private static IPhoneNumberService IdentityPhone()
    {
        var mock = new Mock<IPhoneNumberService>();
        mock.Setup(x => x.Normalize(It.IsAny<string>())).Returns<string>(s => s);
        return mock.Object;
    }

    private SendInvitationCommandHandler Handler(IPhoneNumberService? phone = null)
        => new(_context, phone ?? IdentityPhone());

    private async Task SeedOwnerAndWorkerAsync()
    {
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(LegalEntityId, OwnerId));
        _context.Users.Add(CompanyTestFactory.NewUser(OwnerId, "+380000000001"));
        _context.Users.Add(CompanyTestFactory.NewUser(WorkerId, WorkerPhone));
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task Send_ReturnsOwnerCompanyNotFound_WhenOwnerHasNoLegalEntity()
    {
        var result = await Handler().HandleAsync(new SendInvitationCommand(OwnerId, WorkerPhone));
        result.Status.Should().Be("OwnerCompanyNotFound");
    }

    [Fact]
    public async Task Send_ReturnsInvalidPhone_WhenNormalizeThrows()
    {
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(LegalEntityId, OwnerId));
        await _context.SaveChangesAsync();

        var phone = new Mock<IPhoneNumberService>();
        phone.Setup(x => x.Normalize(It.IsAny<string>())).Throws(new ArgumentException("bad"));

        var result = await Handler(phone.Object).HandleAsync(new SendInvitationCommand(OwnerId, "garbage"));
        result.Status.Should().Be("InvalidPhone");
    }

    [Fact]
    public async Task Send_ReturnsWorkerNotFound_WhenNoRegisteredUser()
    {
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(LegalEntityId, OwnerId));
        await _context.SaveChangesAsync();

        var result = await Handler().HandleAsync(new SendInvitationCommand(OwnerId, WorkerPhone));
        result.Status.Should().Be("WorkerNotFound");
    }

    [Fact]
    public async Task Send_ReturnsCannotInviteSelf_WhenPhoneResolvesToOwner()
    {
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(LegalEntityId, OwnerId));
        _context.Users.Add(CompanyTestFactory.NewUser(OwnerId, WorkerPhone));
        await _context.SaveChangesAsync();

        var result = await Handler().HandleAsync(new SendInvitationCommand(OwnerId, WorkerPhone));
        result.Status.Should().Be("CannotInviteSelf");
    }

    [Fact]
    public async Task Send_ReturnsWorkerAlreadyMember_WhenWorkerBelongsToACompany()
    {
        await SeedOwnerAndWorkerAsync();
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), LegalEntityId, WorkerId));
        await _context.SaveChangesAsync();

        var result = await Handler().HandleAsync(new SendInvitationCommand(OwnerId, WorkerPhone));
        result.Status.Should().Be("WorkerAlreadyMember");
    }

    [Fact]
    public async Task Send_ReturnsAlreadyPending_WhenPendingInvitationExists()
    {
        await SeedOwnerAndWorkerAsync();
        _context.CompanyInvitations.Add(CompanyTestFactory.NewInvitation(
            Guid.NewGuid(), LegalEntityId, OwnerId, WorkerId, WorkerPhone, InvitationStatus.Pending));
        await _context.SaveChangesAsync();

        var result = await Handler().HandleAsync(new SendInvitationCommand(OwnerId, WorkerPhone));
        result.Status.Should().Be("AlreadyPending");
    }

    [Fact]
    public async Task Send_CreatesPendingInvitation_OnSuccess()
    {
        await SeedOwnerAndWorkerAsync();
        // A previously declined invitation must not block a fresh one (only Pending blocks).
        _context.CompanyInvitations.Add(CompanyTestFactory.NewInvitation(
            Guid.NewGuid(), LegalEntityId, OwnerId, WorkerId, WorkerPhone, InvitationStatus.Declined));
        await _context.SaveChangesAsync();

        var result = await Handler().HandleAsync(new SendInvitationCommand(OwnerId, WorkerPhone));

        result.Status.Should().Be("Success");
        result.InvitationId.Should().NotBeNull();
        var created = await _context.CompanyInvitations.FindAsync(result.InvitationId!.Value);
        created!.Status.Should().Be(InvitationStatus.Pending);
        created.WorkerUserId.Should().Be(WorkerId);
    }
}
