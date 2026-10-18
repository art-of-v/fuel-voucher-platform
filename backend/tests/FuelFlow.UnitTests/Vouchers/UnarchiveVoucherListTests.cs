using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.GetAdminVouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Vouchers;

/// <summary>
/// #217 made every voucher delete a soft one, and <c>delete_all</c> flags every row - but nothing
/// could put one back, so a mistaken bulk delete was permanent through the UI.
/// <para>
/// The un-archive write itself is covered in <c>UnarchiveVoucherIntegrationTests</c>: it is an
/// <c>ExecuteUpdateAsync</c> whose WHERE clause carries the <c>IsDeleted</c> predicate, and the EF
/// Core in-memory brand does not translate <c>ExecuteUpdate</c>. This file covers the read side,
/// which the in-memory brand is fine for.
/// </para>
/// </summary>
public sealed class UnarchiveVoucherListTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public UnarchiveVoucherListTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        SeedFuelTypes();
    }

    /// <summary>
    /// The admin list <c>Include</c>s FuelType, so the in-memory brand needs the row to exist -
    /// without it the vouchers silently drop out of the result set.
    /// </summary>
    private void SeedFuelTypes()
    {
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "okko-95",
            Name = "A-95",
            BrandId = "okko",
            BasePrice = 55,
            DiscountPrice = 52,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    private async Task SeedAsync(bool archived)
    {
        _context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Brand = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50m,
            SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = $"OKKO-{Guid.NewGuid():N}"[..20],
            QrPayload = Guid.NewGuid().ToString(),
            Status = VoucherStatus.Available,
            IsDeleted = archived,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Without <c>IsDeleted</c> on the DTO the admin list could not tell an archived row from a live
    /// one. The list query calls <c>IgnoreQueryFilters</c> precisely so an archived voucher stays
    /// findable to un-archive it - which means archived rows were being rendered next to real stock
    /// looking perfectly normal, and an operator had no way to spot one.
    /// </summary>
    [Fact]
    public async Task The_Admin_List_Tells_Archived_Rows_Apart_From_Live_Ones()
    {
        await SeedAsync(archived: true);
        await SeedAsync(archived: false);


        var handler = new GetAdminVouchersQueryHandler(_context);
        var result = await handler.HandleAsync(new GetAdminVouchersQuery());

        result.Data.Should().HaveCount(2, "the list is built with IgnoreQueryFilters so archived rows stay findable");
        result.Data.Should().ContainSingle(v => v.IsDeleted);
        result.Data.Should().ContainSingle(v => !v.IsDeleted);
    }

    public void Dispose() => _context.Dispose();
}