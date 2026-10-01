using FluentAssertions;
using FuelFlow.Features.Company.BlockWorkerVoucher;
using FuelFlow.Features.Company.UnblockWorkerVoucher;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;

namespace FuelFlow.UnitTests.Company;

/// <summary>
/// Epic #103 S3b: an owner freezes/unfreezes a single voucher currently held by one of their
/// workers — one toggle Assigned↔Blocked, keeping the worker link (recall-to-pool stays a
/// separate action). This suite pins the pure guard branches both handlers evaluate before the
/// state transition — ownership/scoping via <c>OwnerCompanyResolver</c> and the state preconditions.
/// The transition itself uses atomic <c>ExecuteUpdateAsync</c> (race-safe against a concurrent
/// redemption), which the InMemory provider cannot run — the durable round-trip, the owner-only
/// visibility rule, and the block-vs-redeem race live in
/// <c>CompanyVoucherBlockingIntegrationTests</c> on real Postgres.
/// </summary>
public sealed class CompanyVoucherBlockingTests : IDisposable
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

    // ---- Block guards -------------------------------------------------------

    [Fact]
    public async Task Block_ReturnsCompanyNotOwned_WhenEntityNotOwned()
    {
        await SeedTwoOwnedCompaniesAsync();

        var result = await new BlockWorkerVoucherCommandHandler(_context, new FuelFlowMetrics())
            .HandleAsync(new BlockWorkerVoucherCommand(OwnerId, Guid.NewGuid(), StrangerEntityId));

        result.Status.Should().Be("CompanyNotOwned");
    }

    [Fact]
    public async Task Block_ReturnsNotFound_WhenVoucherDoesNotExist()
    {
        await SeedTwoOwnedCompaniesAsync();

        var result = await new BlockWorkerVoucherCommandHandler(_context, new FuelFlowMetrics())
            .HandleAsync(new BlockWorkerVoucherCommand(OwnerId, Guid.NewGuid(), SecondEntityId));

        result.Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task Block_ReturnsForbidden_WhenVoucherBelongsToADifferentOwnedEntity()
    {
        await SeedTwoOwnedCompaniesAsync();
        var workerId = Guid.NewGuid();
        // Voucher lives under the FIRST entity, but the caller selected the SECOND.
        var voucher = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: FirstEntityId, assignedToUserId: OwnerId, workerUserId: workerId);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var result = await new BlockWorkerVoucherCommandHandler(_context, new FuelFlowMetrics())
            .HandleAsync(new BlockWorkerVoucherCommand(OwnerId, voucher.Id, SecondEntityId));

        result.Status.Should().Be("Forbidden");
    }

    [Fact]
    public async Task Block_ReturnsInvalidState_WhenVoucherIsInThePool_NotHeldByAWorker()
    {
        await SeedTwoOwnedCompaniesAsync();
        var voucher = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: SecondEntityId, assignedToUserId: OwnerId, workerUserId: null);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var result = await new BlockWorkerVoucherCommandHandler(_context, new FuelFlowMetrics())
            .HandleAsync(new BlockWorkerVoucherCommand(OwnerId, voucher.Id, SecondEntityId));

        result.Status.Should().Be("InvalidState");
        _context.FuelVouchers.Single(x => x.Id == voucher.Id).Status.Should().Be(VoucherStatus.Assigned);
    }

    // ---- Unblock guards -----------------------------------------------------

    [Fact]
    public async Task Unblock_ReturnsCompanyNotOwned_WhenEntityNotOwned()
    {
        await SeedTwoOwnedCompaniesAsync();

        var result = await new UnblockWorkerVoucherCommandHandler(_context, new FuelFlowMetrics())
            .HandleAsync(new UnblockWorkerVoucherCommand(OwnerId, Guid.NewGuid(), StrangerEntityId));

        result.Status.Should().Be("CompanyNotOwned");
    }

    [Fact]
    public async Task Unblock_ReturnsForbidden_WhenVoucherBelongsToADifferentOwnedEntity()
    {
        await SeedTwoOwnedCompaniesAsync();
        var workerId = Guid.NewGuid();
        var voucher = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Blocked,
            legalEntityId: FirstEntityId, assignedToUserId: OwnerId, workerUserId: workerId);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var result = await new UnblockWorkerVoucherCommandHandler(_context, new FuelFlowMetrics())
            .HandleAsync(new UnblockWorkerVoucherCommand(OwnerId, voucher.Id, SecondEntityId));

        result.Status.Should().Be("Forbidden");
    }

    [Fact]
    public async Task Unblock_ReturnsInvalidState_WhenVoucherIsNotBlocked()
    {
        await SeedTwoOwnedCompaniesAsync();
        var workerId = Guid.NewGuid();
        var voucher = CompanyTestFactory.NewVoucher(Guid.NewGuid(), VoucherStatus.Assigned,
            legalEntityId: SecondEntityId, assignedToUserId: OwnerId, workerUserId: workerId);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var result = await new UnblockWorkerVoucherCommandHandler(_context, new FuelFlowMetrics())
            .HandleAsync(new UnblockWorkerVoucherCommand(OwnerId, voucher.Id, SecondEntityId));

        result.Status.Should().Be("InvalidState");
    }
}
