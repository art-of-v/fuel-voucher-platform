using FluentAssertions;
using FuelFlow.Features.Company.RecallVoucher;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;

namespace FuelFlow.UnitTests.Company;

public sealed class CompanyRecallVoucherCommandHandlerTests : IDisposable
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();

    private readonly ApplicationDbContext _context = CompanyTestFactory.CreateContext();
    private readonly RecallVoucherCommandHandler _handler;

    public CompanyRecallVoucherCommandHandlerTests()
    {
        _handler = new RecallVoucherCommandHandler(_context, new FuelFlowMetrics());
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task SeedCompanyAsync()
    {
        _context.LegalEntities.Add(CompanyTestFactory.NewLegalEntity(LegalEntityId, OwnerId));
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task Recall_ReturnsOwnerCompanyNotFound_WhenOwnerHasNoLegalEntity()
    {
        var result = await _handler.HandleAsync(new RecallVoucherCommand(OwnerId, Guid.NewGuid()));
        result.Status.Should().Be("OwnerCompanyNotFound");
    }

    [Fact]
    public async Task Recall_ReturnsNotFound_WhenVoucherMissing()
    {
        await SeedCompanyAsync();
        var result = await _handler.HandleAsync(new RecallVoucherCommand(OwnerId, Guid.NewGuid()));
        result.Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task Recall_ReturnsForbidden_WhenVoucherBelongsToAnotherCompany()
    {
        await SeedCompanyAsync();
        var v = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: Guid.NewGuid(), assignedToUserId: Guid.NewGuid(), workerUserId: WorkerId);
        _context.FuelVouchers.Add(v);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RecallVoucherCommand(OwnerId, v.Id));
        result.Status.Should().Be("Forbidden");
    }

    [Fact]
    public async Task Recall_ReturnsInvalidState_WhenVoucherWasNeverGifted()
    {
        await SeedCompanyAsync();
        var v = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: LegalEntityId, assignedToUserId: OwnerId, workerUserId: null);
        _context.FuelVouchers.Add(v);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RecallVoucherCommand(OwnerId, v.Id));
        result.Status.Should().Be("InvalidState");
    }

    [Fact]
    public async Task Recall_ClearsWorkerAndReturnsToPool_OnSuccess()
    {
        await SeedCompanyAsync();
        var v = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: LegalEntityId, assignedToUserId: OwnerId, workerUserId: WorkerId);
        _context.FuelVouchers.Add(v);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RecallVoucherCommand(OwnerId, v.Id));

        result.Status.Should().Be("Success");
        var stored = await _context.FuelVouchers.FindAsync(v.Id);
        // After recall the worker link is cleared, so the previously-gifted worker can no longer redeem.
        stored!.WorkerUserId.Should().BeNull();
        stored.Status.Should().Be(VoucherStatus.Assigned);
    }
}
