using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.GetAdminVoucherById;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FuelFlow.UnitTests.Vouchers;

/// <summary>
/// The single-voucher detail endpoint is the second half of #227: the list row answers "who do we
/// owe, what did we pay", and the screen an operator opens for a single voucher has to answer it
/// too. It did not - <c>AdminVoucherDetailDto</c> carried neither field.
///
/// The <see cref="IsTestData"/> test is here rather than in its own file because the fix is the
/// same mapping block. The property was declared on the DTO with a warning that the value "must
/// never reach a paying customer", and the handler never assigned it, so every detail response
/// said <c>false</c>.
/// </summary>
public sealed class AdminVoucherDetailSupplierCostTests : IDisposable
{
    private static readonly Guid SupplierId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string SupplierName = "ФОП Колесніков Сергій Юрійович";

    private readonly ApplicationDbContext _context;
    private readonly Mock<IQrGenerator> _qrGeneratorMock;

    public AdminVoucherDetailSupplierCostTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);

        _qrGeneratorMock = new Mock<IQrGenerator>();
        _qrGeneratorMock.Setup(x => x.GenerateQrCode(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Returns("base64-data");

        _context.Suppliers.Add(new Supplier
        {
            Id = SupplierId,
            Name = SupplierName,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "okko-95",
            Name = "A-95",
            BrandId = BrandIds.Okko,
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

    private FuelVoucher Voucher(decimal? costPerLiter, Guid? supplierId, bool isTestData = false) => new()
    {
        Id = Guid.NewGuid(),
        QrPayload = "qr",
        Liters = 5,
        FuelTypeId = "okko-95",
        Brand = "OKKO",
        SupplierExpirationDate = new DateOnly(2027, 1, 31),
        CustomerExpirationDate = new DateOnly(2027, 1, 31),
        VoucherNumber = "VC-1",
        Status = VoucherStatus.Available,
        CreatedAtUtc = DateTime.UtcNow,
        SupplierId = supplierId,
        CostPerLiter = costPerLiter,
        IsTestData = isTestData
    };

    private async Task<AdminVoucherDetailDto> DetailAsync(FuelVoucher voucher)
    {
        _context.FuelVouchers.Add(voucher);
        await _context.SaveChangesAsync();

        var handler = new GetAdminVoucherByIdQueryHandler(_context, _qrGeneratorMock.Object);
        var result = await handler.HandleAsync(new GetAdminVoucherByIdQuery(voucher.Id));

        result.Should().NotBeNull();
        return result!;
    }

    [Fact]
    public async Task Detail_carries_the_supplier_and_the_cost()
    {
        var detail = await DetailAsync(Voucher(94.90m, SupplierId));

        detail.SupplierId.Should().Be(SupplierId);
        detail.SupplierName.Should().Be(SupplierName);
        detail.CostPerLiter.Should().Be(94.90m);
    }

    [Fact]
    public async Task Detail_keeps_the_brand_and_the_supplier_apart()
    {
        var detail = await DetailAsync(Voucher(50m, SupplierId));

        detail.Brand.Should().Be("OKKO");
        detail.SupplierName.Should().Be(SupplierName).And.NotBe(detail.Brand);
    }

    [Fact]
    public async Task An_unentered_cost_is_null_on_the_detail_too()
    {
        var detail = await DetailAsync(Voucher(null, null));

        detail.CostPerLiter.Should().BeNull("nobody recorded what we paid, which is not a price of zero");
        detail.SupplierId.Should().BeNull();
        detail.SupplierName.Should().BeNull();
    }

    [Fact]
    public async Task A_supplier_we_stopped_buying_from_still_names_itself_on_its_vouchers()
    {
        var supplier = await _context.Suppliers.FirstAsync();
        supplier.IsActive = false;
        await _context.SaveChangesAsync();

        var detail = await DetailAsync(Voucher(60m, SupplierId));

        detail.SupplierName.Should().Be(SupplierName, "an inactive supplier is still the supplier of a voucher it sold");
    }

    [Fact]
    public async Task Emulated_test_data_is_reported_as_such()
    {
        // Was silently always false: the DTO declared the field and the handler never assigned it.
        var detail = await DetailAsync(Voucher(50m, SupplierId, isTestData: true));

        detail.IsTestData.Should().BeTrue("a voucher's code does not exist at the Brand and must not read like paid-for stock");
    }
}