using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.GetUserVouchers;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.MarkVoucherAsUsed;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FuelFlow.UnitTests.Vouchers;

/// <summary>
/// Issuing fuel to a worker sets WorkerUserId and leaves AssignedToUserId on the owner, so the two
/// columns name different people. The wallet list used to match the owner on AssignedToUserId and
/// hand them back QrPayload, QrCodeData and a rendered PNG for fuel they had just given away -
/// while GET /vouchers/{id}/qr refused them with 403 and MarkVoucherAsUsed refused them outright.
/// These pin the rule that ended that split.
/// </summary>
public sealed class VoucherQrScopeTests : IDisposable
{
    private static readonly Guid OwnerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid WorkerId = Guid.Parse("ffffffff-1111-2222-3333-444444444444");

    private readonly ApplicationDbContext _context;

    public VoucherQrScopeTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);

        // The wallet query Includes FuelType, a required navigation. Without a matching catalog
        // row the in-memory provider joins it away and the voucher never reaches the result.
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "okko-95",
            Name = "A-95",
            StationId = "okko",
            BasePrice = 55,
            DiscountPrice = 52,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    /// <summary>A voucher the owner issued to <paramref name="workerId"/>, as the command leaves it.</summary>
    private FuelVoucher IssuedVoucher(Guid? workerId)
    {
        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Brand = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = "OKKO-ISSUE-1",
            QrPayload = "9018$1000$redeemable-instrument",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = OwnerId,
            WorkerUserId = workerId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.FuelVouchers.Add(voucher);
        _context.SaveChanges();
        return voucher;
    }

    private GetUserVouchersCommandHandler BuildWalletHandler(out Mock<IQrGenerator> qrGenerator)
    {
        qrGenerator = new Mock<IQrGenerator>();
        qrGenerator.Setup(x => x.GenerateQrCode(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Returns("RENDERED");
        var logger = new Mock<ILogger<GetUserVouchersCommandHandler>>().Object;
        return new GetUserVouchersCommandHandler(_context, qrGenerator.Object, logger);
    }

    [Fact]
    public async Task The_issuer_does_not_receive_the_qr_of_fuel_it_issued_to_a_worker()
    {
        var issued = IssuedVoucher(workerId: WorkerId);
        var handler = BuildWalletHandler(out var qrGenerator);

        var response = await handler.HandleAsync(new GetUserVouchersCommand(OwnerId));

        // The row itself stays: the owner's hub reads this same list to show what each order
        // delivered and how much is still undistributed in the company's pool.
        response.Vouchers.Should().ContainSingle();
        var dto = response.Vouchers[0];
        dto.Id.Should().Be(issued.Id);
        dto.QrPayload.Should().BeEmpty();
        dto.QrCodeData.Should().BeEmpty();
        dto.ImageUrl.Should().BeNull();
        qrGenerator.Verify(x => x.GenerateQrCode(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task The_worker_receives_the_qr_of_the_fuel_it_was_issued()
    {
        IssuedVoucher(workerId: WorkerId);
        var handler = BuildWalletHandler(out _);

        var response = await handler.HandleAsync(new GetUserVouchersCommand(WorkerId));

        response.Vouchers.Should().ContainSingle();
        var dto = response.Vouchers[0];
        dto.QrPayload.Should().Be("9018$1000$redeemable-instrument");
        dto.QrCodeData.Should().Be("9018$1000$redeemable-instrument");
        dto.ImageUrl.Should().Be("data:image/png;base64,RENDERED");
        dto.Source.Should().Be("gifted");
    }

    [Fact]
    public async Task An_owner_keeps_the_qr_of_fuel_still_in_their_own_pool()
    {
        IssuedVoucher(workerId: null);
        var handler = BuildWalletHandler(out _);

        var response = await handler.HandleAsync(new GetUserVouchersCommand(OwnerId));

        response.Vouchers.Should().ContainSingle();
        response.Vouchers[0].QrPayload.Should().Be("9018$1000$redeemable-instrument");
        response.Vouchers[0].ImageUrl.Should().Be("data:image/png;base64,RENDERED");
    }

    /// <summary>
    /// The rule the three redemption routes share. The write that burns a voucher and the two reads
    /// that hand out its QR all go through VoucherHolder, so this pins what they must agree on: a
    /// worker, once set, displaces the owner who issued the fuel.
    /// </summary>
    [Theory]
    [InlineData(true)]   // issued to a worker: the worker holds it, the owner does not
    [InlineData(false)]  // still in the pool: the owner holds it, the worker does not
    public void The_holder_is_the_worker_once_the_fuel_is_issued_to_one(bool issued)
    {
        var workerId = issued ? WorkerId : (Guid?)null;

        VoucherHolder.CanRedeem(workerId, OwnerId, WorkerId).Should().Be(issued);
        VoucherHolder.CanRedeem(workerId, OwnerId, OwnerId).Should().Be(!issued);
        VoucherHolder.CanRedeem(workerId, OwnerId, Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void An_unissued_voucher_has_no_holder_at_all()
    {
        VoucherHolder.Of(null, null).Should().BeNull();
        VoucherHolder.CanRedeem(null, null, OwnerId).Should().BeFalse();
    }
}