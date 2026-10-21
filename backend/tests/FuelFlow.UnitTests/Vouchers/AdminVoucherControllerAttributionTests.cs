using System.Security.Claims;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.DeleteVoucher;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.GetAdminVoucherById;
using FuelFlow.Features.Vouchers.GetAdminVoucherLifecycle;
using FuelFlow.Features.Vouchers.GetAdminVouchers;
using FuelFlow.Features.Vouchers.GetVoucherVerification;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers.UnblockVoucher;
using FuelFlow.Features.Vouchers.UnarchiveVoucher;
using FuelFlow.Features.Vouchers.UpdateVoucher;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.UnitTests.Vouchers;

/// <summary>
/// <c>AdminVoucherController.GetUserId</c> used to return <see cref="Guid.Empty"/> when the token
/// carried no subject. That value is NOT NULL, so it satisfied <c>ck_voucher_events_actor_named</c> on
/// its own, and every admin write that reached the ledger was attributed to a user who does not exist.
///
/// <para>
/// #1210 closed the storage side — a CHECK now refuses the zero GUID — and the writers were taught to
/// treat it as "no actor" and skip the event. This is the fix under that: the action is refused rather
/// than performed anonymously. The distinction matters most for the destructive one. A skipped audit
/// row leaves a real change with no author; a 401 leaves nothing changed and nothing to explain.
/// </para>
/// </summary>
public sealed class AdminVoucherControllerAttributionTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;
    private readonly Mock<IQrGenerator> _qr = new();
    private readonly Mock<IBackgroundJobClient> _jobs = new();

    public AdminVoucherControllerAttributionTests()
    {
        _context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options, TestQrCipher.Instance);
        _context.Database.EnsureCreated();
        _eventService = new AuditEventService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private AdminVoucherController ControllerFor(ClaimsPrincipal user)
        => new(
            new GetAdminVouchersQueryHandler(_context),
            new GetAdminVoucherByIdQueryHandler(_context, _qr.Object),
            new GetVoucherVerificationQueryHandler(_context, _qr.Object),
            new UnblockVoucherCommandHandler(_context, _eventService),
            new UpdateVoucherCommandHandler(_context, _eventService),
            new DeleteVoucherCommandHandler(_context, _eventService),
            new BulkActionVouchersCommandHandler(_context, _jobs.Object, _eventService),
            new UnarchiveVoucherCommandHandler(_context, _eventService),
            new GetAdminVoucherLifecycleQueryHandler(_context))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };

    /// <summary>Authenticated, authorised, and anonymous as far as the subject goes.</summary>
    private static ClaimsPrincipal NoSubject =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") }, "test"));

    private static ClaimsPrincipal WithSubject(Guid id) =>
        new(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                new Claim(ClaimTypes.Role, "Admin")
            },
            "test"));

    private async Task<Guid> SeedStockVoucherAsync()
    {
        var id = Guid.NewGuid();
        _context.FuelVouchers.Add(new FuelVoucher
        {
            Id = id,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            VoucherNumber = $"V-{id:N}"[..20],
            QrPayload = $"qr-{id:N}",
            Status = VoucherStatus.Available,
            CostPerLiter = 40m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return id;
    }

    /// <summary>
    /// Every write endpoint, because the guard is repeated per action and a future endpoint that forgets
    /// it is the realistic failure. Asserted on the result <em>and</em> on the row: a controller that
    /// changed something and then returned 401 would pass a result-only check.
    /// </summary>
    [Fact]
    public async Task Writes_WithoutASubjectClaim_AreRefused_AndChangeNothing()
    {
        // Delete, Update, Unblock, Unarchive, BulkAction. Each gets its own voucher so the cases do not
        // share state, and each asserts on the row it touched.
        var forDelete = await SeedStockVoucherAsync();
        (await ControllerFor(NoSubject).Delete(forDelete, default))
            .Should().BeOfType<UnauthorizedResult>("Delete must refuse a write it cannot attribute");
        await AssertUntouched(forDelete, v => v.Status, VoucherStatus.Available, "Delete");

        var forUpdate = await SeedStockVoucherAsync();
        (await ControllerFor(NoSubject).Update(
                forUpdate, new UpdateVoucherRequest { Status = nameof(VoucherStatus.Deactivated) }, default))
            .Should().BeOfType<UnauthorizedResult>();
        await AssertUntouched(forUpdate, v => v.Status, VoucherStatus.Available, "Update");

        var forUnblock = await SeedStockVoucherAsync();
        (await ControllerFor(NoSubject).Unblock(forUnblock, default))
            .Should().BeOfType<UnauthorizedResult>();
        await AssertUntouched(forUnblock, v => v.Status, VoucherStatus.Available, "Unblock");

        var forUnarchive = await SeedStockVoucherAsync();
        (await ControllerFor(NoSubject).Unarchive(forUnarchive, default))
            .Should().BeOfType<UnauthorizedResult>();
        await AssertUntouched(forUnarchive, v => v.IsDeleted, false, "Unarchive");

        var forBulk = await SeedStockVoucherAsync();
        (await ControllerFor(NoSubject).BulkAction(
                new BulkActionRequest { Action = "deactivate", Ids = new List<Guid> { forBulk } }, default))
            .Should().BeOfType<UnauthorizedResult>();
        await AssertUntouched(forBulk, v => v.Status, VoucherStatus.Available, "BulkAction");
    }

    private async Task AssertUntouched(
        Guid voucherId, Func<FuelVoucher, object> read, object unchanged, string endpoint)
    {
        var stored = await _context.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucherId);

        read(stored).Should().Be(unchanged, $"{endpoint} changed a voucher it was supposed to refuse");

        (await _context.VoucherEvents.CountAsync(e => e.VoucherId == voucherId))
            .Should().Be(0, $"{endpoint} recorded an event for an action it refused to take");
        (await _context.VoucherMovements.CountAsync(m => m.VoucherId == voucherId))
            .Should().Be(0);
    }

    /// <summary>
    /// The other half, and the one that matters if this ever ships: a real token still works. Every
    /// token <c>JwtTokenService</c> mints carries <c>sub</c>, so if the guard were wrong this is where
    /// an admin would find out — and it is the failure this change could silently cause.
    /// </summary>
    [Fact]
    public async Task ARealSubject_StillReachesTheHandler_AndIsRecorded()
    {
        var actor = Guid.NewGuid();
        var voucherId = await SeedStockVoucherAsync();

        var result = await ControllerFor(WithSubject(actor))
            .Update(voucherId, new UpdateVoucherRequest { Status = nameof(VoucherStatus.Deactivated) }, default);

        result.Should().NotBeOfType<UnauthorizedResult>(
            "the guard must refuse a missing claim, not swallow a present one");

        var recorded = await _context.VoucherEvents.AsNoTracking()
            .SingleAsync(e => e.VoucherId == voucherId);
        recorded.ActorUserId.Should().Be(actor);
    }
}
