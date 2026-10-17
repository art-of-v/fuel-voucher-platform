using FluentAssertions;
using FuelFlow.Features.Company.GiftVouchers;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;

namespace FuelFlow.UnitTests.Company;

public sealed class CompanyGiftVouchersCommandHandlerTests : IDisposable
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();

    private readonly ApplicationDbContext _context = CompanyTestFactory.CreateContext();
    private readonly GiftVouchersCommandHandler _handler;

    public CompanyGiftVouchersCommandHandlerTests()
    {
        _handler = new GiftVouchersCommandHandler(_context, new FuelFlowMetrics());
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task SeedCompanyWithMemberAsync()
    {
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(LegalEntityId, OwnerId));
        _context.CompanyMembers.Add(CompanyTestFactory.NewMember(Guid.NewGuid(), LegalEntityId, WorkerId));
        await _context.SaveChangesAsync();
    }

    private FuelVoucher AddPoolVoucher()
    {
        var v = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: LegalEntityId, assignedToUserId: OwnerId, workerUserId: null);
        _context.FuelVouchers.Add(v);
        return v;
    }

    [Fact]
    public async Task Gift_ReturnsEmptyVoucherList_WhenNoVoucherIds()
    {
        var result = await _handler.HandleAsync(new GiftVouchersCommand(OwnerId, WorkerId, Array.Empty<Guid>()));
        result.Status.Should().Be("EmptyVoucherList");
    }

    [Fact]
    public async Task Gift_ReturnsOwnerCompanyNotFound_WhenOwnerHasNoLegalEntity()
    {
        var result = await _handler.HandleAsync(new GiftVouchersCommand(OwnerId, WorkerId, new[] { Guid.NewGuid() }));
        result.Status.Should().Be("OwnerCompanyNotFound");
    }

    [Fact]
    public async Task Gift_ReturnsWorkerNotMember_WhenWorkerIsNotInCompany()
    {
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(LegalEntityId, OwnerId));
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new GiftVouchersCommand(OwnerId, WorkerId, new[] { Guid.NewGuid() }));
        result.Status.Should().Be("WorkerNotMember");
    }

    [Fact]
    public async Task Gift_ReturnsVoucherNotFound_WhenAnyVoucherIdMissing()
    {
        await SeedCompanyWithMemberAsync();
        var present = AddPoolVoucher();
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(
            new GiftVouchersCommand(OwnerId, WorkerId, new[] { present.Id, Guid.NewGuid() }));
        result.Status.Should().Be("VoucherNotFound");
    }

    [Fact]
    public async Task Gift_ReturnsVoucherNotEligible_WhenVoucherAlreadyAssignedToAWorker()
    {
        await SeedCompanyWithMemberAsync();
        var v = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: LegalEntityId, assignedToUserId: OwnerId, workerUserId: Guid.NewGuid());
        _context.FuelVouchers.Add(v);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new GiftVouchersCommand(OwnerId, WorkerId, new[] { v.Id }));
        result.Status.Should().Be("VoucherNotEligible");
    }

    [Fact]
    public async Task Gift_ReturnsVoucherNotEligible_WhenVoucherBelongsToAnotherCompany()
    {
        await SeedCompanyWithMemberAsync();
        var v = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: Guid.NewGuid(), assignedToUserId: OwnerId, workerUserId: null);
        _context.FuelVouchers.Add(v);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new GiftVouchersCommand(OwnerId, WorkerId, new[] { v.Id }));
        result.Status.Should().Be("VoucherNotEligible");
    }

    [Fact]
    public async Task Gift_AssignsWorkerAndReturnsCount_OnSuccess()
    {
        await SeedCompanyWithMemberAsync();
        var v1 = AddPoolVoucher();
        var v2 = AddPoolVoucher();
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new GiftVouchersCommand(OwnerId, WorkerId, new[] { v1.Id, v2.Id }));

        result.Status.Should().Be("Success");
        result.GiftedCount.Should().Be(2);

        var stored = _context.FuelVouchers.Where(x => x.Id == v1.Id || x.Id == v2.Id).ToList();
        stored.Should().OnlyContain(x => x.WorkerUserId == WorkerId);
        // Gifted vouchers stay assigned to the owner but are now worker-scoped: the owner can no
        // longer redeem them (enforced at redemption by MarkVoucherAsUsed's worker check).
        stored.Should().OnlyContain(x => x.AssignedToUserId == OwnerId && x.Status == VoucherStatus.Assigned);
    }

    /// <summary>
    /// D-13. A gift is the change of place that <c>voucher_movements</c> exists to record, and
    /// <c>Gift</c> was the last kind with no writer — so before this, the ledger could say where a
    /// voucher was but never that a company handed one to a worker.
    /// <para>
    /// The locations are the point of the assertions. The eligibility guard required
    /// <c>LegalEntityId == this company</c> and <c>WorkerUserId == null</c>, which is the company
    /// holding stock; the worker's id is what the write sets. Anything else would be a movement that
    /// contradicts the guard that let it through.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Gift_RecordsTheHandover_AsAMovement()
    {
        await SeedCompanyWithMemberAsync();
        var voucher = AddPoolVoucher();
        await _context.SaveChangesAsync();

        await _handler.HandleAsync(new GiftVouchersCommand(OwnerId, WorkerId, new[] { voucher.Id }));

        var movement = _context.VoucherMovements.Single(m => m.VoucherId == voucher.Id);
        movement.Kind.Should().Be(VoucherMovementKinds.Gift);
        movement.FromLocation.Should().Be(VoucherLocations.Company);
        movement.ToLocation.Should().Be(VoucherLocations.Worker);
        movement.ActorKind.Should().Be(VoucherActorKinds.User);
        movement.ActorUserId.Should().Be(OwnerId);
        movement.ActorPartyId.Should().Be(LegalEntityId, "the company is the acting party here, not just a user");
        movement.OrderId.Should().NotBeNull("the gift re-homes the voucher onto the issuance order");

        // A gift changes who holds the paper, not what it is: the status stays Assigned. An event
        // claiming a transition here would be exactly the row the exchange refuses to write.
        _context.VoucherEvents.Should().NotContain(e => e.VoucherId == voucher.Id);
    }
}
