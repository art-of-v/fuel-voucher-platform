using FluentAssertions;
using FuelFlow.Features.Company.ReturnVoucherToPool;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;

namespace FuelFlow.UnitTests.Company;

/// <summary>
/// The guards of the way back to the pool (planning #558). The transition itself is a
/// compare-and-swap <c>ExecuteUpdateAsync</c>, which the in-memory provider does not
/// implement, so the durable behaviour is pinned by the integration suite
/// (<c>CompanyFiredWorkerFuelIntegrationTests</c>) and this file is about the refusals.
/// </summary>
public sealed class CompanyReturnVoucherToPoolCommandHandlerTests : IDisposable
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid WorkerId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly Guid OrderId = Guid.NewGuid();

    private readonly ApplicationDbContext _context = CompanyTestFactory.CreateContext();
    private readonly ReturnVoucherToPoolCommandHandler _handler;

    public CompanyReturnVoucherToPoolCommandHandlerTests()
    {
        _handler = new ReturnVoucherToPoolCommandHandler(_context, new FuelFlowMetrics());
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
    public async Task Return_ReturnsOwnerCompanyNotFound_WhenOwnerHasNoLegalEntity()
    {
        var result = await _handler.HandleAsync(new ReturnVoucherToPoolCommand(OwnerId, Guid.NewGuid()));
        result.Status.Should().Be("OwnerCompanyNotFound");
    }

    [Fact]
    public async Task Return_ReturnsNotFound_WhenVoucherMissing()
    {
        await SeedCompanyAsync();
        var result = await _handler.HandleAsync(new ReturnVoucherToPoolCommand(OwnerId, Guid.NewGuid()));
        result.Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task Return_ReturnsForbidden_WhenVoucherBelongsToAnotherCompany()
    {
        await SeedCompanyAsync();
        var v = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Blocked,
            legalEntityId: Guid.NewGuid(), assignedToUserId: Guid.NewGuid(), workerUserId: WorkerId,
            orderId: OrderId);
        _context.FuelVouchers.Add(v);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new ReturnVoucherToPoolCommand(OwnerId, v.Id));
        result.Status.Should().Be("Forbidden");
    }

    [Fact]
    public async Task Return_ReturnsInvalidState_WhenTheVoucherIsNotFrozen()
    {
        // An active voucher in a worker's hands is the recall's business - it never froze,
        // so there is nothing to take back.
        await SeedCompanyAsync();
        var v = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: LegalEntityId, assignedToUserId: OwnerId, workerUserId: WorkerId,
            orderId: OrderId);
        _context.FuelVouchers.Add(v);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new ReturnVoucherToPoolCommand(OwnerId, v.Id));
        result.Status.Should().Be("InvalidState");
    }

    [Fact]
    public async Task Return_ReturnsInvalidState_WhenTheVoucherHasNoOrder()
    {
        // The pool files fuel under the purchase it arrived with, so a voucher with no
        // order has nowhere to go.
        await SeedCompanyAsync();
        var v = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Blocked,
            legalEntityId: LegalEntityId, assignedToUserId: OwnerId, workerUserId: WorkerId);
        _context.FuelVouchers.Add(v);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new ReturnVoucherToPoolCommand(OwnerId, v.Id));
        result.Status.Should().Be("InvalidState");
    }
}
