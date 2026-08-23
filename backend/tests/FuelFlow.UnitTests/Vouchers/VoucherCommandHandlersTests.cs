using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.GetInventory;
using FuelFlow.Features.Vouchers.GetUserVouchers;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.MarkVoucherAsUsed;
using FuelFlow.Features.Vouchers.RestoreVoucher;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FuelFlow.UnitTests.Vouchers;

public sealed class VoucherCommandHandlersTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OtherUserId = Guid.Parse("ffffffff-1111-2222-3333-444444444444");

    private readonly ApplicationDbContext _context;

    public VoucherCommandHandlersTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        SeedFuelTypes();
    }

    private GetUserVouchersCommandHandler BuildGetUserVouchersHandler()
    {
        var qrGeneratorMock = new Mock<IQrGenerator>();
        qrGeneratorMock.Setup(x => x.GenerateQrCode(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Returns("qr-code-data");
        var logger = new Mock<ILogger<GetUserVouchersCommandHandler>>().Object;
        return new GetUserVouchersCommandHandler(_context, qrGeneratorMock.Object, logger);
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

    [Fact]
    public async Task GetUserVouchers_ShouldReturnAssignedAndUsedVouchers()
    {
        var assignedVoucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = "OKKO-1",
            QrPayload = "payload-1",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = UserId,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5),
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-5)
        };

        var usedVoucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-dp",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)),
            VoucherNumber = "OKKO-2",
            QrPayload = "payload-2",
            Status = VoucherStatus.Used,
            AssignedToUserId = UserId,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };

        var availableVoucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            VoucherNumber = "OKKO-3",
            QrPayload = "payload-3",
            Status = VoucherStatus.Available,
            AssignedToUserId = null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.FuelVouchers.AddRange(assignedVoucher, usedVoucher, availableVoucher);
        await _context.SaveChangesAsync();

        var handler = BuildGetUserVouchersHandler();
        var command = new GetUserVouchersCommand(UserId);

        var response = await handler.HandleAsync(command);

        response.Vouchers.Should().HaveCount(2);
        response.Vouchers.Should().Contain(v => v.Id == assignedVoucher.Id);
        response.Vouchers.Should().Contain(v => v.Id == usedVoucher.Id);
        response.Vouchers.Should().NotContain(v => v.Id == availableVoucher.Id);
        response.Vouchers[0].CreatedAtUtc.Should().BeAfter(response.Vouchers[1].CreatedAtUtc);
    }

    [Fact]
    public async Task GetUserVouchers_ShouldReturnEmptyList_WhenNoVouchersAssigned()
    {
        var handler = BuildGetUserVouchersHandler();
        var command = new GetUserVouchersCommand(OtherUserId);

        var response = await handler.HandleAsync(command);

        response.Vouchers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserVouchers_ShouldReturnGiftedVoucherWithGiftedSource()
    {
        var worker = new User
        {
            Id = UserId,
            PhoneNumber = "+10000000001",
            FirstName = "Ivan",
            LastName = "Petrenko",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var giftedVoucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 20,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = "OKKO-GIFT-1",
            QrPayload = "gift-payload-1",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = OtherUserId,
            WorkerUserId = UserId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.Users.Add(worker);
        _context.FuelVouchers.Add(giftedVoucher);
        await _context.SaveChangesAsync();

        var handler = BuildGetUserVouchersHandler();
        var response = await handler.HandleAsync(new GetUserVouchersCommand(UserId));

        response.Vouchers.Should().ContainSingle();
        response.Vouchers[0].Id.Should().Be(giftedVoucher.Id);
        response.Vouchers[0].Source.Should().Be("gifted");
        response.Vouchers[0].WorkerUserId.Should().Be(UserId);
        response.Vouchers[0].WorkerFirstName.Should().Be("Ivan");
        response.Vouchers[0].WorkerLastName.Should().Be("Petrenko");
    }

    [Fact]
    public async Task GetInventory_ShouldGroupByProviderFuelTypeLiters()
    {
        var vouchers = new[]
        {
            CreateVoucher("OKKO", "okko-95", 50, VoucherStatus.Available),
            CreateVoucher("OKKO", "okko-95", 50, VoucherStatus.Assigned),
            CreateVoucher("OKKO", "okko-95", 50, VoucherStatus.Used),
            CreateVoucher("OKKO", "okko-dp", 50, VoucherStatus.Available),
            CreateVoucher("WOG", "wog-95", 50, VoucherStatus.Available)
        };

        _context.FuelVouchers.AddRange(vouchers);
        await _context.SaveChangesAsync();

        var handler = new GetInventoryCommandHandler(_context);
        var command = new GetInventoryCommand();

        var response = await handler.HandleAsync(command);

        response.Inventory.Should().HaveCount(3);

        var okkoA95 = response.Inventory.First(i => i.Provider == "OKKO" && i.FuelTypeId == "okko-95");
        okkoA95.Available.Should().Be(1);
        okkoA95.Assigned.Should().Be(1);
        okkoA95.Used.Should().Be(1);
        okkoA95.Total.Should().Be(3);

        var okkoDiesel = response.Inventory.First(i => i.Provider == "OKKO" && i.FuelTypeId == "okko-dp");
        okkoDiesel.Available.Should().Be(1);
        okkoDiesel.Total.Should().Be(1);

        var wogA95 = response.Inventory.First(i => i.Provider == "WOG" && i.FuelTypeId == "wog-95");
        wogA95.Available.Should().Be(1);
        wogA95.Total.Should().Be(1);
    }

    // MarkVoucherAsUsed_ShouldTransitionFromAssignedToUsed moved to
    // FuelFlow.IntegrationTests.MarkVoucherAsUsedConcurrencyIntegrationTests. The handler now performs
    // the Assigned -> Used transition with an atomic conditional ExecuteUpdateAsync (WP-4), which the
    // EF Core in-memory provider cannot translate. The tests below all return before that write, so
    // they still exercise the in-memory path.

    [Fact]
    public async Task MarkVoucherAsUsed_ShouldBeIdempotent_WhenAlreadyUsed()
    {
        var voucherId = Guid.NewGuid();

        var voucher = new FuelVoucher
        {
            Id = voucherId,
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = "OKKO-1",
            QrPayload = "payload-1",
            Status = VoucherStatus.Used,
            AssignedToUserId = UserId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new MarkVoucherAsUsedCommandHandler(_context);
        var command = new MarkVoucherAsUsedCommand(voucherId, UserId);

        var response = await handler.HandleAsync(command);

        response.Success.Should().BeTrue();
        response.Message.Should().Be("Voucher already marked as used");
    }

    [Fact]
    public async Task MarkVoucherAsUsed_ShouldFail_WhenVoucherNotAssignedToUser()
    {
        var voucherId = Guid.NewGuid();

        var voucher = new FuelVoucher
        {
            Id = voucherId,
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = "OKKO-1",
            QrPayload = "payload-1",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = OtherUserId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new MarkVoucherAsUsedCommandHandler(_context);
        var command = new MarkVoucherAsUsedCommand(voucherId, UserId);

        var response = await handler.HandleAsync(command);

        response.Success.Should().BeFalse();
        response.Message.Should().Be("Voucher is not assigned to this user");
        response.ErrorCode.Should().Be("Forbidden");
    }

    [Fact]
    public async Task MarkVoucherAsUsed_ShouldFail_WhenGiftedVoucherIsUsedByOwnerInsteadOfWorker()
    {
        var voucherId = Guid.NewGuid();

        var voucher = new FuelVoucher
        {
            Id = voucherId,
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = "OKKO-GIFT-OWNER",
            QrPayload = "payload-gift-owner",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = UserId,
            WorkerUserId = OtherUserId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new MarkVoucherAsUsedCommandHandler(_context);
        var response = await handler.HandleAsync(new MarkVoucherAsUsedCommand(voucherId, UserId));

        response.Success.Should().BeFalse();
        response.Message.Should().Be("Only the assigned worker can use this voucher");
        response.ErrorCode.Should().Be("Forbidden");

        var unchangedVoucher = await _context.FuelVouchers.FindAsync(voucherId);
        unchangedVoucher!.Status.Should().Be(VoucherStatus.Assigned);
    }

    [Fact]
    public async Task RestoreVoucher_ShouldTransitionFromUsedToAssigned()
    {
        var voucherId = Guid.NewGuid();

        var voucher = new FuelVoucher
        {
            Id = voucherId,
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = "OKKO-1",
            QrPayload = "payload-1",
            Status = VoucherStatus.Used,
            AssignedToUserId = UserId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new RestoreVoucherCommandHandler(_context);
        var command = new RestoreVoucherCommand(voucherId);

        var response = await handler.HandleAsync(command);

        response.Success.Should().BeTrue();
        response.Message.Should().Be("Voucher restored to Assigned status");

        var updatedVoucher = await _context.FuelVouchers.FindAsync(voucherId);
        updatedVoucher!.Status.Should().Be(VoucherStatus.Assigned);
    }

    [Fact]
    public async Task RestoreVoucher_ShouldBeIdempotent_WhenAlreadyAssigned()
    {
        var voucherId = Guid.NewGuid();

        var voucher = new FuelVoucher
        {
            Id = voucherId,
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = "OKKO-1",
            QrPayload = "payload-1",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = UserId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new RestoreVoucherCommandHandler(_context);
        var command = new RestoreVoucherCommand(voucherId);

        var response = await handler.HandleAsync(command);

        response.Success.Should().BeTrue();
        response.Message.Should().Be("Voucher is already in Assigned status");
    }

    [Fact]
    public async Task RestoreVoucher_ShouldFail_WhenVoucherIsAvailable()
    {
        var voucherId = Guid.NewGuid();

        var voucher = new FuelVoucher
        {
            Id = voucherId,
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = "OKKO-1",
            QrPayload = "payload-1",
            Status = VoucherStatus.Available,
            AssignedToUserId = null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new RestoreVoucherCommandHandler(_context);
        var command = new RestoreVoucherCommand(voucherId);

        var response = await handler.HandleAsync(command);

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("cannot be restored");
    }

    private FuelVoucher CreateVoucher(string provider, string fuelTypeId, decimal liters, VoucherStatus status)
    {
        return new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = $"{provider}-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = status,
            AssignedToUserId = status == VoucherStatus.Assigned || status == VoucherStatus.Used ? UserId : null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }
}
