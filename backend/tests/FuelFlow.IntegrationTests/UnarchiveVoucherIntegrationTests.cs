using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers.UnarchiveVoucher;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Covers the undo for #217's soft delete. <c>delete_all</c> flags every voucher in the system and,
/// until this existed, nothing could clear the flag - a mistaken bulk delete was permanent through
/// the UI.
/// <para>
/// These live in the integration suite rather than the unit suite deliberately: the write is an
/// <c>ExecuteUpdateAsync</c> behind the global <c>!IsDeleted</c> query filter, and the EF Core
/// in-memory brand neither translates <c>ExecuteUpdate</c> nor models the filter.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class UnarchiveVoucherIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public UnarchiveVoucherIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task An_Archived_Voucher_Comes_Back_Into_The_Ordinary_Query()
    {
        var voucherId = await SeedAsync(VoucherStatus.Available, archived: true);

        await using (var context = CreateContext())
        {
            var handler = new UnarchiveVoucherCommandHandler(context, new AuditEventService(context));
            var result = await handler.HandleAsync(new UnarchiveVoucherCommand(voucherId));

            result.Should().NotBeNull();
            result!.Success.Should().BeTrue();
        }

        // The point of the whole change: an ordinary, filter-respecting lookup now sees it again.
        // That is what a customer redeeming the code will go through.
        await using var verify = CreateContext();
        var visible = await verify.FuelVouchers.Where(v => v.Id == voucherId).ToListAsync();
        visible.Should().HaveCount(1, "the voucher has to be reachable again through the normal query path");

        await using var raw = CreateContext();
        var stored = await raw.FuelVouchers
            .IgnoreQueryFilters()
            .Where(v => v.Id == voucherId)
            .Select(v => new { v.IsDeleted, v.UpdatedAtUtc })
            .SingleAsync();

        stored.IsDeleted.Should().BeFalse();
        stored.UpdatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// The distinction from <c>RestoreVoucherCommandHandler</c>. That one undoes a *redemption* and
    /// moves the status back to Assigned; this undoes a *deletion* and must put the voucher back
    /// exactly as it was. A voucher archived while Available that came back Assigned would let
    /// whoever restored it hand out fuel nobody had bought.
    /// </summary>
    [Theory]
    [InlineData(VoucherStatus.Available)]
    [InlineData(VoucherStatus.Imported)]
    public async Task The_Status_Survives_The_Restore(VoucherStatus status)
    {
        var voucherId = await SeedAsync(status, archived: true);

        await using var context = CreateContext();
        var handler = new UnarchiveVoucherCommandHandler(context, new AuditEventService(context));
        await handler.HandleAsync(new UnarchiveVoucherCommand(voucherId));

        await using var verify = CreateContext();
        var stored = await verify.FuelVouchers
            .IgnoreQueryFilters()
            .Where(v => v.Id == voucherId)
            .Select(v => new { v.Status, v.IsDeleted })
            .SingleAsync();

        stored.IsDeleted.Should().BeFalse();
        stored.Status.Should().Be(status);
    }

    [Fact]
    public async Task Restoring_A_Visible_Voucher_Is_Still_Success()
    {
        var voucherId = await SeedAsync(VoucherStatus.Available, archived: false);

        await using var context = CreateContext();
        var handler = new UnarchiveVoucherCommandHandler(context, new AuditEventService(context));
        var result = await handler.HandleAsync(new UnarchiveVoucherCommand(voucherId));

        // The operator's intent - "this voucher should be visible" - already holds, so reporting an
        // error would make a harmless re-click look like a failure.
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task A_Missing_Voucher_Is_Null_So_The_Controller_Can_Answer_404()
    {
        await using var context = CreateContext();
        var handler = new UnarchiveVoucherCommandHandler(context, new AuditEventService(context));

        var result = await handler.HandleAsync(new UnarchiveVoucherCommand(Guid.NewGuid()));

        result.Should().BeNull();
    }

    /// <summary>
    /// An un-delete is exactly the kind of action an operator later has to explain, so it leaves an
    /// audit row rather than silently flipping a flag.
    /// </summary>
    [Fact]
    public async Task The_Unarchive_Is_Recorded_In_The_Audit_Trail()
    {
        var voucherId = await SeedAsync(VoucherStatus.Available, archived: true);
        var adminId = Guid.NewGuid();

        await using var context = CreateContext();
        var handler = new UnarchiveVoucherCommandHandler(context, new AuditEventService(context));
        await handler.HandleAsync(new UnarchiveVoucherCommand(voucherId, adminId, "Operator"));

        await using var verify = CreateContext();
        var recorded = await verify.Set<AuditEventOutbox>()
            .Where(e => e.EventType == "VoucherUnarchived")
            .ToListAsync();

        recorded.Should().ContainSingle("an un-delete must be explainable afterwards");
        recorded[0].ChangedByUserId.Should().Be(adminId);
    }

    private async Task<Guid> SeedAsync(VoucherStatus status, bool archived)
    {
        await using var seed = CreateContext();
        await ResetDataAsync(seed);

        var voucherId = Guid.NewGuid();
        seed.FuelVouchers.Add(new FuelVoucher
        {
            Id = voucherId,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = $"OKKO-{voucherId:N}"[..20],
            QrPayload = $"payload-{voucherId:N}",
            Status = status,
            IsDeleted = archived,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await seed.SaveChangesAsync();
        return voucherId;
    }

    private static async Task ResetDataAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}