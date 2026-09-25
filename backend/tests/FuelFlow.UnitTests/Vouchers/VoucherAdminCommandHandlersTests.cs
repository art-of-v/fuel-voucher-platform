using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.DeleteVoucher;
using FuelFlow.Features.Vouchers.GetAdminVoucherById;
using FuelFlow.Features.Vouchers.GetAdminVouchers;
using FuelFlow.Features.Vouchers.GetFuelVouchers;
using FuelFlow.Features.Vouchers.GetImportBatches;
using FuelFlow.Features.Vouchers.GetQrCodes;
using FuelFlow.Features.Vouchers.GetVoucherVerification;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers.UnblockVoucher;
using FuelFlow.Features.Vouchers.UpdateVoucher;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace FuelFlow.UnitTests.Vouchers;

public sealed class VoucherAdminCommandHandlersTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OtherUserId = Guid.Parse("ffffffff-1111-2222-3333-444444444444");

    private readonly ApplicationDbContext _context;
    private readonly Mock<IQrGenerator> _qrGeneratorMock;
    private readonly Mock<IBackgroundJobClient> _backgroundJobClientMock;
    private readonly ProviderEventService _eventService;

    public VoucherAdminCommandHandlersTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        SeedFuelTypes();

        _qrGeneratorMock = new Mock<IQrGenerator>();
        _qrGeneratorMock.Setup(x => x.GenerateQrCode(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Returns("base64-data");

        _backgroundJobClientMock = new Mock<IBackgroundJobClient>();
        _backgroundJobClientMock.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("job-id");

        _eventService = new ProviderEventService(_context);
    }

    private void SeedFuelTypes()
    {
        var fuelTypes = new[]
        {
            new FuelTypeEntity { Id = "okko-dp", Name = "ДП ЄВРО", StationId = "okko", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "okko-95", Name = "A-95", StationId = "okko", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "okko-p95", Name = "Pulls 95", StationId = "okko", BasePrice = 62, DiscountPrice = 58, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "wog-dp", Name = "ДП Mustang", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "wog-95", Name = "A-95 Mustang", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow }
        };
        _context.FuelTypes.AddRange(fuelTypes);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private FuelVoucher CreateVoucher(
        string provider = "OKKO",
        string fuelTypeId = "okko-95",
        VoucherStatus status = VoucherStatus.Available,
        DateTime? createdAtUtc = null,
        Guid? importJobId = null,
        string? voucherNumber = null)
    {
        return new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = voucherNumber ?? $"{provider}-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = status,
            AssignedToUserId = status == VoucherStatus.Assigned || status == VoucherStatus.Used ? UserId : null,
            ImportJobId = importJobId,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    // ── 1. DeleteVoucherCommandHandler ───────────────────────────────────────

    [Fact]
    public async Task DeleteVoucher_ShouldSoftDeleteAndReturnSuccess()
    {
        var voucher = CreateVoucher();
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new DeleteVoucherCommandHandler(_context, _eventService);
        var result = await handler.HandleAsync(new DeleteVoucherCommand(voucher.Id));

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();

        var updated = await _context.FuelVouchers.IgnoreQueryFilters().FirstAsync(v => v.Id == voucher.Id);
        updated.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteVoucher_ShouldReturnNull_WhenVoucherMissing()
    {
        var handler = new DeleteVoucherCommandHandler(_context, _eventService);

        var result = await handler.HandleAsync(new DeleteVoucherCommand(Guid.NewGuid()));

        result.Should().BeNull();
    }

    // ── 2. UpdateVoucherCommandHandler ───────────────────────────────────────

    [Fact]
    public async Task UpdateVoucher_ShouldUpdateStatusToUsed()
    {
        var voucher = CreateVoucher(status: VoucherStatus.Assigned);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new UpdateVoucherCommandHandler(_context, _eventService);
        var result = await handler.HandleAsync(new UpdateVoucherCommand(voucher.Id, "Used", null));

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();

        var updated = await _context.FuelVouchers.FirstAsync(v => v.Id == voucher.Id);
        updated.Status.Should().Be(VoucherStatus.Used);
    }

    [Fact]
    public async Task UpdateVoucher_ShouldReturnNull_WhenVoucherMissing()
    {
        var handler = new UpdateVoucherCommandHandler(_context, _eventService);

        var result = await handler.HandleAsync(new UpdateVoucherCommand(Guid.NewGuid(), "Used", null));

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateVoucher_ShouldIgnoreInvalidStatusString()
    {
        var voucher = CreateVoucher(status: VoucherStatus.Available);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new UpdateVoucherCommandHandler(_context, _eventService);
        var result = await handler.HandleAsync(new UpdateVoucherCommand(voucher.Id, "NotAStatus", null));

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();

        var updated = await _context.FuelVouchers.FirstAsync(v => v.Id == voucher.Id);
        updated.Status.Should().Be(VoucherStatus.Available);
    }

    [Fact]
    public async Task UpdateVoucher_WithActingAdmin_ShouldRecordAuditEvent()
    {
        var voucher = CreateVoucher(status: VoucherStatus.Assigned);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new UpdateVoucherCommandHandler(_context, _eventService);
        var result = await handler.HandleAsync(
            new UpdateVoucherCommand(voucher.Id, "Used", null, UserId, "Admin User"));

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();

        var evt = _context.ProviderEventOutbox.Single(e => e.EventType == "VoucherUpdated");
        evt.AggregateType.Should().Be("Voucher");
        evt.AggregateId.Should().Be(voucher.Id.ToString());
        evt.ChangedByUserId.Should().Be(UserId);
        evt.ChangedByUserName.Should().Be("Admin User");
        evt.OldValue.Should().Contain("Assigned");
        evt.NewValue.Should().Contain("Used");
    }

    [Fact]
    public async Task UpdateVoucher_ShouldRejectAssigned_WhenNoFulfillmentRecord()
    {
        var voucher = CreateVoucher(status: VoucherStatus.Available);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new UpdateVoucherCommandHandler(_context, _eventService);
        var result = await handler.HandleAsync(new UpdateVoucherCommand(voucher.Id, "Assigned", UserId));

        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Error.Should().Contain("fulfillment");

        var updated = await _context.FuelVouchers.FirstAsync(v => v.Id == voucher.Id);
        updated.Status.Should().Be(VoucherStatus.Available);
        updated.AssignedToUserId.Should().BeNull();
    }

    [Fact]
    public async Task UpdateVoucher_ShouldAllowAssigned_WhenFulfillmentRecordExists()
    {
        var orderId = Guid.NewGuid();
        var voucher = CreateVoucher(status: VoucherStatus.Available, voucherNumber: "OKKO-FULFILL-1");
        _context.FuelVouchers.Add(voucher);
        _context.Orders.Add(new Order
        {
            Id = orderId,
            UserId = UserId,
            Price = 2500,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.Fulfillments.Add(new Fulfillment
        {
            OrderId = orderId,
            VoucherId = voucher.Id,
            FulfilledAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var handler = new UpdateVoucherCommandHandler(_context, _eventService);
        var result = await handler.HandleAsync(new UpdateVoucherCommand(voucher.Id, "Assigned", UserId));

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();

        var updated = await _context.FuelVouchers.FirstAsync(v => v.Id == voucher.Id);
        updated.Status.Should().Be(VoucherStatus.Assigned);
        updated.AssignedToUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task DeleteVoucher_WithActingAdmin_ShouldRecordAuditEvent()
    {
        var voucher = CreateVoucher(voucherNumber: "OKKO-AUDIT-1");
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new DeleteVoucherCommandHandler(_context, _eventService);
        var result = await handler.HandleAsync(new DeleteVoucherCommand(voucher.Id, UserId, "Admin User"));

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();

        var evt = _context.ProviderEventOutbox.Single(e => e.EventType == "VoucherDeleted");
        evt.AggregateId.Should().Be(voucher.Id.ToString());
        evt.OldValue.Should().Contain("OKKO-AUDIT-1");
        evt.ProviderId.Should().Be("OKKO");
    }

    [Fact]
    public async Task BulkAction_Assign_ShouldBeRejected_WhenNoFulfillmentRecord()
    {
        var voucher = CreateVoucher(status: VoucherStatus.Available);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new BulkActionVouchersCommandHandler(_context, _backgroundJobClientMock.Object, _eventService);
        var result = await handler.HandleAsync(
            new BulkActionVouchersCommand("assign", [voucher.Id], OtherUserId, UserId, "Admin User"));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("disabled");

        var updated = await _context.FuelVouchers.FirstAsync(v => v.Id == voucher.Id);
        updated.Status.Should().Be(VoucherStatus.Available);
        updated.AssignedToUserId.Should().BeNull();

        _context.ProviderEventOutbox.Should().NotContain(e => e.EventType == "VoucherBulkAction");
    }

    // ── 3. BulkActionVouchersCommandHandler ──────────────────────────────────

    [Fact]
    public async Task BulkAction_Activate_ShouldSetAvailable()
    {
        var imported = CreateVoucher(status: VoucherStatus.Imported);
        var assigned = CreateVoucher(status: VoucherStatus.Assigned);
        _context.FuelVouchers.AddRange(imported, assigned);
        await _context.SaveChangesAsync();

        var handler = new BulkActionVouchersCommandHandler(_context, _backgroundJobClientMock.Object, _eventService);
        var result = await handler.HandleAsync(new BulkActionVouchersCommand("activate", [imported.Id, assigned.Id], null));

        result.Success.Should().BeTrue();
        result.Count.Should().Be(2);

        var vouchers = await _context.FuelVouchers.Where(v => v.Id == imported.Id || v.Id == assigned.Id).ToListAsync();
        vouchers.Should().OnlyContain(v => v.Status == VoucherStatus.Available);

        // #30: activate re-drives fulfillment via the direct enqueue (verified below); it must NOT
        // write a VoucherActivated outbox row — nothing consumes that type, so orphan rows only
        // accumulated as permanently-unprocessed and tripped the reconciliation "unprocessed outbox" alert.
        _context.OutboxEvents.Should().NotContain(e => e.EventType == OutboxEventType.VoucherActivated);
        _backgroundJobClientMock.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Once);
    }

    [Fact]
    public async Task BulkAction_Expire_ShouldSetExpired()
    {
        var voucher = CreateVoucher(status: VoucherStatus.Available);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new BulkActionVouchersCommandHandler(_context, _backgroundJobClientMock.Object, _eventService);
        var result = await handler.HandleAsync(new BulkActionVouchersCommand("expire", [voucher.Id], null));

        result.Success.Should().BeTrue();
        result.Count.Should().Be(1);

        var updated = await _context.FuelVouchers.FirstAsync(v => v.Id == voucher.Id);
        updated.Status.Should().Be(VoucherStatus.Expired);

        _backgroundJobClientMock.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task BulkAction_Deactivate_ShouldSetDeactivated()
    {
        var voucher = CreateVoucher(status: VoucherStatus.Available);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new BulkActionVouchersCommandHandler(_context, _backgroundJobClientMock.Object, _eventService);
        var result = await handler.HandleAsync(new BulkActionVouchersCommand("deactivate", [voucher.Id], null));

        result.Success.Should().BeTrue();
        result.Count.Should().Be(1);

        var deactivated = await _context.FuelVouchers.FirstAsync(v => v.Id == voucher.Id);
        deactivated.Status.Should().Be(VoucherStatus.Deactivated);
    }

    [Fact]
    public async Task BulkAction_Assign_ShouldRejectAndKeepVoucherUnchanged()
    {
        var voucher = CreateVoucher(status: VoucherStatus.Available, createdAtUtc: DateTime.UtcNow);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new BulkActionVouchersCommandHandler(_context, _backgroundJobClientMock.Object, _eventService);
        var result = await handler.HandleAsync(new BulkActionVouchersCommand("assign", [voucher.Id], OtherUserId));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("disabled");

        var updated = await _context.FuelVouchers.FirstAsync(v => v.Id == voucher.Id);
        updated.Status.Should().Be(VoucherStatus.Available);
        updated.AssignedToUserId.Should().BeNull();
    }

    [Fact]
    public async Task BulkAction_DeleteAll_ShouldRemoveAllVouchers()
    {
        _context.FuelVouchers.AddRange(CreateVoucher(), CreateVoucher());
        await _context.SaveChangesAsync();

        var handler = new BulkActionVouchersCommandHandler(_context, _backgroundJobClientMock.Object, _eventService);
        var result = await handler.HandleAsync(new BulkActionVouchersCommand("delete_all", null, null));

        result.Success.Should().BeTrue();
        result.Count.Should().Be(0);

        (await _context.FuelVouchers.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task BulkAction_ShouldReturnError_WhenUnknownAction()
    {
        var voucher = CreateVoucher(status: VoucherStatus.Available);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new BulkActionVouchersCommandHandler(_context, _backgroundJobClientMock.Object, _eventService);
        var result = await handler.HandleAsync(new BulkActionVouchersCommand("bogus", [voucher.Id], null));

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Unknown action: bogus");

        var updated = await _context.FuelVouchers.FirstAsync(v => v.Id == voucher.Id);
        updated.Status.Should().Be(VoucherStatus.Available);
    }

    // ── 4. GetAdminVoucherByIdQueryHandler ───────────────────────────────────

    [Fact]
    public async Task GetAdminVoucherById_ShouldReturnDtoWithMatchingId()
    {
        var voucher = CreateVoucher(status: VoucherStatus.Assigned);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new GetAdminVoucherByIdQueryHandler(_context, _qrGeneratorMock.Object);
        var result = await handler.HandleAsync(new GetAdminVoucherByIdQuery(voucher.Id));

        result.Should().NotBeNull();
        result!.Id.Should().Be(voucher.Id);
        result.VoucherNumber.Should().Be(voucher.VoucherNumber);
        result.Status.Should().Be(VoucherStatus.Assigned.ToString());
        result.FuelType.Should().NotBeNull();
        result.FuelType!.Name.Should().Be("A-95");
        result.QrImage.Should().Be("data:image/png;base64,base64-data");
    }

    [Fact]
    public async Task GetAdminVoucherById_ShouldReturnNull_WhenVoucherMissing()
    {
        var handler = new GetAdminVoucherByIdQueryHandler(_context, _qrGeneratorMock.Object);

        var result = await handler.HandleAsync(new GetAdminVoucherByIdQuery(Guid.NewGuid()));

        result.Should().BeNull();
    }

    // ── 5. GetAdminVouchersQueryHandler ──────────────────────────────────────

    [Fact]
    public async Task GetAdminVouchers_ShouldReturnPagedListAndTotal()
    {
        _context.FuelVouchers.AddRange(
            CreateVoucher(createdAtUtc: DateTime.UtcNow),
            CreateVoucher(createdAtUtc: DateTime.UtcNow.AddDays(-1)),
            CreateVoucher(createdAtUtc: DateTime.UtcNow.AddDays(-2)));
        await _context.SaveChangesAsync();

        var handler = new GetAdminVouchersQueryHandler(_context);
        var result = await handler.HandleAsync(new GetAdminVouchersQuery(Page: 1, Limit: 2));

        result.Total.Should().Be(3);
        result.GlobalTotal.Should().Be(3);
        result.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAdminVouchers_ShouldFilterByStatus()
    {
        var available = CreateVoucher(status: VoucherStatus.Available);
        var used = CreateVoucher(status: VoucherStatus.Used);
        _context.FuelVouchers.AddRange(available, used);
        await _context.SaveChangesAsync();

        var handler = new GetAdminVouchersQueryHandler(_context);
        var result = await handler.HandleAsync(new GetAdminVouchersQuery(Status: "Available"));

        result.Total.Should().Be(1);
        result.Data.Should().ContainSingle(d => d.Id == available.Id);
    }

    [Fact]
    public async Task GetAdminVouchers_ShouldFilterByWorkerAndReturnWorkerDetails()
    {
        var worker = new User
        {
            Id = OtherUserId,
            PhoneNumber = "+10000000002",
            FirstName = "Ivan",
            LastName = "Petrenko",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var gifted = CreateVoucher(status: VoucherStatus.Assigned, voucherNumber: "OKKO-GIFT-1");
        gifted.WorkerUserId = OtherUserId;

        var poolVoucher = CreateVoucher(status: VoucherStatus.Assigned, voucherNumber: "OKKO-POOL-1");
        poolVoucher.WorkerUserId = null;

        _context.Users.Add(worker);
        _context.FuelVouchers.AddRange(gifted, poolVoucher);
        await _context.SaveChangesAsync();

        var handler = new GetAdminVouchersQueryHandler(_context);
        var result = await handler.HandleAsync(new GetAdminVouchersQuery(WorkerUserId: OtherUserId));

        result.Total.Should().Be(1);
        result.Data.Should().ContainSingle();
        result.Data[0].Id.Should().Be(gifted.Id);
        result.Data[0].WorkerUserId.Should().Be(OtherUserId);
        result.Data[0].WorkerFirstName.Should().Be("Ivan");
        result.Data[0].WorkerLastName.Should().Be("Petrenko");
        result.Statuses.Should().Contain("Blocked");
    }

    // ── 6. GetFuelVouchersQueryHandler ───────────────────────────────────────

    [Fact]
    public async Task UnblockVoucher_ShouldTransitionBlockedToAssigned()
    {
        var voucher = CreateVoucher(status: VoucherStatus.Blocked, voucherNumber: "OKKO-BLOCK-1");
        voucher.AssignedToUserId = UserId;
        voucher.WorkerUserId = OtherUserId;
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new UnblockVoucherCommandHandler(_context, _eventService);
        var result = await handler.HandleAsync(new UnblockVoucherCommand(voucher.Id, UserId, "Admin User"));

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();

        var updated = await _context.FuelVouchers.FirstAsync(v => v.Id == voucher.Id);
        updated.Status.Should().Be(VoucherStatus.Assigned);
        updated.AssignedToUserId.Should().Be(UserId);
        updated.WorkerUserId.Should().BeNull();

        var evt = _context.ProviderEventOutbox.Single(e => e.EventType == "VoucherUnblocked");
        evt.AggregateId.Should().Be(voucher.Id.ToString());
        evt.ChangedByUserId.Should().Be(UserId);
        evt.ChangedByUserName.Should().Be("Admin User");
        evt.OldValue.Should().Contain("Blocked");
        evt.NewValue.Should().Contain("Assigned");
    }

    [Fact]
    public async Task UnblockVoucher_ShouldRejectWhenVoucherIsNotBlocked()
    {
        var voucher = CreateVoucher(status: VoucherStatus.Assigned, voucherNumber: "OKKO-ASSIGNED-1");
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new UnblockVoucherCommandHandler(_context, _eventService);
        var result = await handler.HandleAsync(new UnblockVoucherCommand(voucher.Id));

        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Error.Should().Contain("Only blocked vouchers can be unblocked");

        var updated = await _context.FuelVouchers.FirstAsync(v => v.Id == voucher.Id);
        updated.Status.Should().Be(VoucherStatus.Assigned);
    }

    [Fact]
    public async Task UnblockVoucher_ShouldReturnNull_WhenVoucherMissing()
    {
        var handler = new UnblockVoucherCommandHandler(_context, _eventService);

        var result = await handler.HandleAsync(new UnblockVoucherCommand(Guid.NewGuid()));

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetFuelVouchers_ShouldReturnItemsAndTotal()
    {
        _context.FuelVouchers.AddRange(CreateVoucher(), CreateVoucher());
        await _context.SaveChangesAsync();

        var handler = new GetFuelVouchersQueryHandler(_context);
        var result = await handler.HandleAsync(new GetFuelVouchersQuery(null, null, 1, 50));

        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetFuelVouchers_ShouldFilterByVoucherNumberSearch()
    {
        var target = CreateVoucher(voucherNumber: "OKKO-TARGET-1");
        var other = CreateVoucher(voucherNumber: "OKKO-OTHER-1");
        _context.FuelVouchers.AddRange(target, other);
        await _context.SaveChangesAsync();

        var handler = new GetFuelVouchersQueryHandler(_context);
        var result = await handler.HandleAsync(new GetFuelVouchersQuery("OKKO-TARGET", null, 1, 50));

        result.Total.Should().Be(1);
        result.Items.Should().ContainSingle(v => v.Id == target.Id);
    }

    // ── 7. GetVoucherVerificationQueryHandler ────────────────────────────────

    [Fact]
    public async Task GetVoucherVerification_ShouldReturnVerificationData()
    {
        var voucher = CreateVoucher(status: VoucherStatus.Assigned, voucherNumber: "OKKO-VERIFY-1");
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new GetVoucherVerificationQueryHandler(_context, _qrGeneratorMock.Object);
        var result = await handler.HandleAsync(new GetVoucherVerificationQuery(voucher.Id));

        result.Should().NotBeNull();
        result!.VoucherId.Should().Be(voucher.Id);
        result.VoucherNumber.Should().Be("OKKO-VERIFY-1");
        result.Status.Should().Be(VoucherStatus.Assigned.ToString());
        result.QrCodeBase64.Should().Be("data:image/png;base64,base64-data");
    }

    // ── 8. GetQrCodesQueryHandler ────────────────────────────────────────────

    [Fact]
    public async Task GetQrCodes_ShouldReturnSequentialIdsAndCorrectStatus()
    {
        _context.FuelVouchers.AddRange(
            CreateVoucher(status: VoucherStatus.Available, createdAtUtc: DateTime.UtcNow),
            CreateVoucher(status: VoucherStatus.Assigned, createdAtUtc: DateTime.UtcNow.AddMinutes(-1)),
            CreateVoucher(status: VoucherStatus.Used, createdAtUtc: DateTime.UtcNow.AddMinutes(-2)));
        await _context.SaveChangesAsync();

        var handler = new GetQrCodesQueryHandler(_context);
        var result = await handler.HandleAsync(new GetQrCodesQuery());

        result.Should().HaveCount(3);
        result.Select(d => d.Id).Should().Equal(1, 2, 3);

        result[0].Status.Should().Be("available");
        result[0].StationId.Should().Be("okko");
        result[0].FuelType.Should().Be("A-95");
        result[1].Status.Should().Be("sold");
        result[2].Status.Should().Be("sold");
    }

    // ── 9. Import batches ────────────────────────────────────────────────────

    private VoucherImport CreateImport(string fileName = "vouchers.pdf", int importedCount = 0)
    {
        var import = new VoucherImport
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            PageCount = 1,
            StartedAtUtc = DateTime.UtcNow.AddHours(-1),
            CompletedAtUtc = DateTime.UtcNow,
            Status = "Completed",
            ImportedCount = importedCount,
            DuplicateCount = 0,
            FailedCount = 0
        };
        return import;
    }

    [Fact]
    public async Task GetImportBatches_ShouldReturnListWithVoucherCounts()
    {
        var import = CreateImport(importedCount: 1);
        var voucher = CreateVoucher(importJobId: import.Id);
        _context.VoucherImports.Add(import);
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new GetImportBatchesQueryHandler(_context);
        var result = await handler.HandleAsync(new GetImportBatchesQuery());

        result.Should().ContainSingle();
        result[0].Id.Should().Be(import.Id);
        result[0].FileName.Should().Be("vouchers.pdf");
        result[0].Status.Should().Be("Completed");
        result[0].VoucherCount.Should().Be(1);
    }

    [Fact]
    public async Task GetImportBatchById_ShouldReturnDto()
    {
        var import = CreateImport(importedCount: 2);
        _context.VoucherImports.Add(import);
        await _context.SaveChangesAsync();

        var handler = new GetImportBatchByIdQueryHandler(_context);
        var result = await handler.HandleAsync(new GetImportBatchByIdQuery(import.Id));

        result.Should().NotBeNull();
        result!.Id.Should().Be(import.Id);
        result.FileName.Should().Be("vouchers.pdf");
        result.PageCount.Should().Be(1);
    }

    [Fact]
    public async Task GetImportBatchById_ShouldReturnNull_WhenMissing()
    {
        var handler = new GetImportBatchByIdQueryHandler(_context);

        var result = await handler.HandleAsync(new GetImportBatchByIdQuery(Guid.NewGuid()));

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetImportBatchVouchers_ShouldReturnVouchersForImport()
    {
        var import = CreateImport(importedCount: 2);
        _context.VoucherImports.Add(import);
        _context.FuelVouchers.AddRange(
            CreateVoucher(importJobId: import.Id, voucherNumber: "OKKO-IMP-1"),
            CreateVoucher(importJobId: import.Id, voucherNumber: "OKKO-IMP-2"),
            CreateVoucher(importJobId: Guid.NewGuid(), voucherNumber: "OKKO-OTHER-1"));
        await _context.SaveChangesAsync();

        var handler = new GetImportBatchVouchersQueryHandler(_context);
        var result = await handler.HandleAsync(new GetImportBatchVouchersQuery(import.Id));

        result.Should().HaveCount(2);
        result.Select(v => v.VoucherNumber).Should().Contain("OKKO-IMP-1");
        result.Select(v => v.VoucherNumber).Should().NotContain("OKKO-OTHER-1");
    }

    // ── 10. ImportVouchersCommandHandler ─────────────────────────────────────

    private static async IAsyncEnumerable<PageRender> YieldPages(params PageRender[] pages)
    {
        foreach (var page in pages)
            yield return page;
    }

    private ImportVouchersCommandHandler BuildImportHandler(
        IPdfRenderer pdfRenderer,
        IVoucherDetector detector,
        IQrDecoder qrDecoder,
        IEnumerable<IVoucherProviderParser> parsers)
    {
        var logger = new Mock<ILogger<ImportVouchersCommandHandler>>().Object;
        return new ImportVouchersCommandHandler(_context, pdfRenderer, detector, qrDecoder, parsers, logger, _backgroundJobClientMock.Object, new FuelFlow.SharedKernel.Observability.FuelFlowMetrics(), FuelFlow.SharedKernel.Observability.NotificationDispatcher.Disabled);
    }

    private PageRender CreatePageRender()
    {
        var image = new Image<Rgba32>(100, 100);
        return new PageRender
        {
            PageNumber = 1,
            Image = image,
            WidthPoints = 200,
            HeightPoints = 200,
            Words = Array.Empty<Word>()
        };
    }

    private static VoucherRegion CreateRegion() => new()
    {
        Bounds = new Rectangle(0, 0, 100, 100),
        PdfBounds = new PdfRectangle(0, 0, 200, 200)
    };

    private ParsedVoucher CreateParsedVoucher(string voucherNumber, string qrPayload)
    {
        return new ParsedVoucher
        {
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = voucherNumber,
            QrPayload = qrPayload,
            Confidence = 100,
            OriginalQrMatrix = null
        };
    }

    [Fact]
    public async Task ImportVouchers_ShouldImportVouchers_WhenParsedSuccessfully()
    {
        var voucherNumber = "OKKO-2026-0001";
        var parsed = CreateParsedVoucher(voucherNumber, $"{voucherNumber}|qrpayload");
        var pageRender = CreatePageRender();
        var region = CreateRegion();

        var pdfRendererMock = new Mock<IPdfRenderer>();
        pdfRendererMock.Setup(r => r.RenderPagesAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(YieldPages(pageRender));

        var detectorMock = new Mock<IVoucherDetector>();
        detectorMock.Setup(d => d.Detect(It.IsAny<PageRender>()))
            .Returns(new[] { region });

        var qrDecoderMock = new Mock<IQrDecoder>();
        qrDecoderMock.Setup(d => d.Decode(It.IsAny<Image>()))
            .Returns(new QrDecodeResult { Text = "decoded" });

        var parserMock = new Mock<IVoucherProviderParser>();
        parserMock.Setup(p => p.CanParse(It.IsAny<ProviderDetectionContext>())).Returns(true);
        parserMock.Setup(p => p.ParseAsync(It.IsAny<ProviderParseContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { parsed } as IReadOnlyCollection<ParsedVoucher>);

        var handler = BuildImportHandler(
            pdfRendererMock.Object, detectorMock.Object, qrDecoderMock.Object, new[] { parserMock.Object });

        var response = await handler.HandleAsync(new ImportVouchersCommand(new MemoryStream(), "vouchers.pdf"), CancellationToken.None);

        response.Imported.Should().Be(1);
        response.Failed.Should().Be(0);
        response.Duplicates.Should().Be(0);

        var saved = await _context.FuelVouchers.IgnoreQueryFilters().ToListAsync();
        saved.Should().ContainSingle();
        saved[0].VoucherNumber.Should().Be(voucherNumber);
        saved[0].Status.Should().Be(VoucherStatus.Imported);
        saved[0].ImportJobId.Should().NotBeNull();

        _backgroundJobClientMock.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Once);
    }

    [Fact]
    public async Task ImportVouchers_ShouldIncrementFailed_WhenNoParserFound()
    {
        var pageRender = CreatePageRender();
        var region = CreateRegion();

        var pdfRendererMock = new Mock<IPdfRenderer>();
        pdfRendererMock.Setup(r => r.RenderPagesAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(YieldPages(pageRender));

        var detectorMock = new Mock<IVoucherDetector>();
        detectorMock.Setup(d => d.Detect(It.IsAny<PageRender>()))
            .Returns(new[] { region });

        var qrDecoderMock = new Mock<IQrDecoder>();

        var handler = BuildImportHandler(
            pdfRendererMock.Object, detectorMock.Object, qrDecoderMock.Object, Array.Empty<IVoucherProviderParser>());

        var response = await handler.HandleAsync(new ImportVouchersCommand(new MemoryStream(), "vouchers.pdf"), CancellationToken.None);

        response.Imported.Should().Be(0);
        response.Failed.Should().Be(1);

        _context.VoucherImportErrors.Should().ContainSingle();
        (await _context.FuelVouchers.IgnoreQueryFilters().ToListAsync()).Should().BeEmpty();

        _backgroundJobClientMock.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task ImportVouchers_ShouldFailImport_WhenDetectionThrows()
    {
        var pageRender = CreatePageRender();

        var pdfRendererMock = new Mock<IPdfRenderer>();
        pdfRendererMock.Setup(r => r.RenderPagesAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(YieldPages(pageRender));

        var detectorMock = new Mock<IVoucherDetector>();
        detectorMock.Setup(d => d.Detect(It.IsAny<PageRender>()))
            .Throws(new InvalidOperationException("detection failed"));

        var qrDecoderMock = new Mock<IQrDecoder>();

        var handler = BuildImportHandler(
            pdfRendererMock.Object, detectorMock.Object, qrDecoderMock.Object, Array.Empty<IVoucherProviderParser>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new ImportVouchersCommand(new MemoryStream(), "vouchers.pdf"), CancellationToken.None));

        var import = await _context.VoucherImports.SingleAsync();
        import.Status.Should().Be("Failed");
        (await _context.FuelVouchers.IgnoreQueryFilters().ToListAsync()).Should().BeEmpty();
    }

    // ── 11. GetVouchersQueryHandler ──────────────────────────────────────────

    [Fact]
    public async Task GetVouchers_ShouldReturnVouchersWithFields()
    {
        var v1 = CreateVoucher(voucherNumber: "OKKO-LIST-1", createdAtUtc: DateTime.UtcNow);
        var v2 = CreateVoucher(voucherNumber: "OKKO-LIST-2", createdAtUtc: DateTime.UtcNow.AddMinutes(-1));
        _context.FuelVouchers.AddRange(v1, v2);
        await _context.SaveChangesAsync();

        var handler = new GetVouchersQueryHandler(_context, _qrGeneratorMock.Object);
        var result = await handler.HandleAsync(new GetVouchersQuery(Page: 1, PageSize: 50), CancellationToken.None);

        result.TotalCount.Should().Be(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(50);
        result.Vouchers.Should().HaveCount(2);

        var first = result.Vouchers.First(v => v.Id == v1.Id);
        first.VoucherNumber.Should().Be("OKKO-LIST-1");
        first.Provider.Should().Be("OKKO");
        first.FuelTypeName.Should().Be("A-95");
        first.QrCodeUrl.Should().Be($"/api/voucher-catalog/{v1.Id}/qr");
    }

    [Fact]
    public async Task GetVouchers_ShouldClampOversizedPageSize()
    {
        _context.FuelVouchers.Add(CreateVoucher(voucherNumber: "OKKO-CLAMP-1"));
        await _context.SaveChangesAsync();

        var handler = new GetVouchersQueryHandler(_context, _qrGeneratorMock.Object);
        var result = await handler.HandleAsync(
            new GetVouchersQuery(Page: 0, PageSize: 1_000_000), CancellationToken.None);

        result.Page.Should().Be(1);
        result.PageSize.Should().Be(GetVouchersQueryHandler.MaxPageSize);
    }

    [Fact]
    public async Task GetVouchers_ShouldFilterByFuelTypeId()
    {
        _context.FuelVouchers.AddRange(
            CreateVoucher(fuelTypeId: "okko-95", voucherNumber: "OKKO-95-1"),
            CreateVoucher(fuelTypeId: "okko-dp", voucherNumber: "OKKO-DP-1"));
        await _context.SaveChangesAsync();

        var handler = new GetVouchersQueryHandler(_context, _qrGeneratorMock.Object);
        var result = await handler.HandleAsync(new GetVouchersQuery(Page: 1, PageSize: 50, FuelTypeId: "okko-dp"), CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.Vouchers.Should().ContainSingle(v => v.FuelTypeId == "okko-dp");
    }
}
