using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.GetAdminVouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.UnitTests.Vouchers;

/// <summary>
/// #227: the voucher list showed the brand and nothing else, so an operator could not see who we
/// bought a voucher from or what it cost. A campaign had marked that "PASS" by reading the
/// scenario's intent rather than its response body.
///
/// The test that matters here is the second one. It is easy to add the fields and default them to
/// zero, which would render as "cost 0,00 ₴" for every voucher whose cost was never entered -
/// telling the operator a free-of-charge voucher rather than an unknown one.
/// </summary>
public sealed class AdminVoucherListSupplierCostTests : IDisposable
{
    private static readonly Guid SupplierId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string SupplierName = "ФОП Колесніков Сергій Юрійович";

    private readonly ApplicationDbContext _context;

    public AdminVoucherListSupplierCostTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);

        _context.Suppliers.Add(new Supplier
        {
            EdrIpn = "9000000104",
            Id = SupplierId,
            Name = SupplierName,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("okko-95"),
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

    private FuelVoucher Voucher(string number, decimal? costPerLiter, Guid? supplierId) => new()
    {
        Id = Guid.NewGuid(),
        QrPayload = "qr",
        Liters = 5,
        FuelTypeId = ToUuid("okko-95"),
        Brand = "OKKO",
        SupplierExpirationDate = new DateOnly(2027, 1, 31),
        CustomerExpirationDate = new DateOnly(2027, 1, 31),
        VoucherNumber = number,
        Status = VoucherStatus.Available,
        CreatedAtUtc = DateTime.UtcNow,
        SupplierId = supplierId,
        CostPerLiter = costPerLiter
    };

    private async Task<AdminVoucherListItemDto> SingleAsync()
    {
        var handler = new GetAdminVouchersQueryHandler(_context);
        var response = await handler.HandleAsync(new GetAdminVouchersQuery());
        response.Data.Should().HaveCount(1, "the test seeds exactly one voucher");
        return response.Data[0];
    }

    [Fact]
    public async Task List_row_carries_the_supplier_and_the_cost()
    {
        _context.FuelVouchers.Add(Voucher("VC-1", 94.90m, SupplierId));
        await _context.SaveChangesAsync();

        var row = await SingleAsync();

        row.SupplierId.Should().Be(SupplierId);
        row.SupplierName.Should().Be(SupplierName);
        row.CostPerLiter.Should().Be(94.90m);
    }

    [Fact]
    public async Task An_unentered_cost_stays_null_rather_than_reading_as_zero()
    {
        // A voucher imported before costs were recorded: no supplier, no cost. The row must say so
        // instead of implying it was free.
        _context.FuelVouchers.Add(Voucher("VC-2", null, null));
        await _context.SaveChangesAsync();

        var row = await SingleAsync();

        row.CostPerLiter.Should().BeNull("an unknown cost is not a cost of zero");
        row.SupplierId.Should().BeNull();
        row.SupplierName.Should().BeNull();
    }

    [Fact]
    public async Task Brand_and_supplier_are_reported_as_different_facts()
    {
        // The two were confused for a while: the brand is printed on the voucher, the supplier is who
        // we owe. A brand name must never be passed off as the supplier's name.
        _context.FuelVouchers.Add(Voucher("VC-3", 50m, SupplierId));
        await _context.SaveChangesAsync();

        var row = await SingleAsync();

        row.Brand.Should().Be("OKKO", "the brand is still the brand");
        row.SupplierName.Should().Be(SupplierName).And.NotBe(row.Brand);
    }

    [Fact]
    public async Task A_supplier_we_stopped_buying_from_still_names_itself_on_its_vouchers()
    {
        // A supplier is deactivated, not deleted - the vouchers it sold are still paid-for paper. The
        // row has to keep naming it, because "who did we buy this from" is a question about the past
        // and stays answerable when the supplier stops being one we buy from.
        var supplier = await _context.Suppliers.FirstAsync();
        supplier.IsActive = false;
        _context.FuelVouchers.Add(Voucher("VC-4", 60m, SupplierId));
        await _context.SaveChangesAsync();

        var row = await SingleAsync();

        row.SupplierName.Should().Be(SupplierName, "an inactive supplier is still the supplier of a voucher it sold");
    }
}
