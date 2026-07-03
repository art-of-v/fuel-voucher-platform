using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.GetInventory;
using FuelFlow.Features.Vouchers.GetUserVouchers;
using FuelFlow.Features.Vouchers.MarkVoucherAsUsed;
using FuelFlow.Features.Vouchers.RestoreVoucher;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Vouchers;

public sealed class VoucherCommandHandlersTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public VoucherCommandHandlersTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        SeedFuelTypes();
    }

    private void SeedFuelTypes()
    {
        var fuelTypes = new[]
        {
            new FuelTypeEntity { Id = "okko-dp", Name = "ДП ЄВРО", StationId = "okko", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "okko-95", Name = "A-95", StationId = "okko", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "okko-p95", Name = "Pulls 95", StationId = "okko", BasePrice = 62, DiscountPrice = 58, CreatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "wog-dp", Name = "ДП Mustang", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "wog-95", Name = "A-95 Mustang", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = DateTime.UtcNow }
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
        var userId = "user-123";

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
            AssignedToUserId = userId,
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
            AssignedToUserId = userId,
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

        var handler = new GetUserVouchersCommandHandler(_context);
        var command = new GetUserVouchersCommand(userId);

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
        var userId = "user-no-vouchers";

        var handler = new GetUserVouchersCommandHandler(_context);
        var command = new GetUserVouchersCommand(userId);

        var response = await handler.HandleAsync(command);

        response.Vouchers.Should().BeEmpty();
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

    [Fact]
    public async Task MarkVoucherAsUsed_ShouldTransitionFromAssignedToUsed()
    {
        var userId = "user-123";
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
            AssignedToUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new MarkVoucherAsUsedCommandHandler(_context);
        var command = new MarkVoucherAsUsedCommand(voucherId, userId);

        var response = await handler.HandleAsync(command);

        response.Success.Should().BeTrue();
        response.Message.Should().Be("Voucher marked as used");

        var updatedVoucher = await _context.FuelVouchers.FindAsync(voucherId);
        updatedVoucher!.Status.Should().Be(VoucherStatus.Used);
        updatedVoucher.UpdatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task MarkVoucherAsUsed_ShouldBeIdempotent_WhenAlreadyUsed()
    {
        var userId = "user-123";
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
            AssignedToUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new MarkVoucherAsUsedCommandHandler(_context);
        var command = new MarkVoucherAsUsedCommand(voucherId, userId);

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
            AssignedToUserId = "other-user",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new MarkVoucherAsUsedCommandHandler(_context);
        var command = new MarkVoucherAsUsedCommand(voucherId, "user-123");

        var response = await handler.HandleAsync(command);

        response.Success.Should().BeFalse();
        response.Message.Should().Be("Voucher is not assigned to this user");
    }

    [Fact]
    public async Task RestoreVoucher_ShouldTransitionFromUsedToAssigned()
    {
        var userId = "user-123";
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
            AssignedToUserId = userId,
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
        var userId = "user-123";
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
            AssignedToUserId = userId,
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
            AssignedToUserId = status == VoucherStatus.Assigned || status == VoucherStatus.Used ? "user-123" : null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }
}


