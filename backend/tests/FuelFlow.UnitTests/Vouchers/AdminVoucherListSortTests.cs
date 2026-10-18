using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.GetAdminVouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Vouchers;

/// <summary>
/// #227 shipped the supplier and the cost as columns; the list still could not be ordered by them,
/// which for an operator finding the expensive batch of vouchers is the whole point of the column.
/// Three things are worth pinning here, and one of them is not about the new columns at all:
///
/// 1. An unrecorded cost sorts last in both directions. Postgres puts NULLs first on a descending
///    sort, so the naive implementation buries the un-priced vouchers in the middle of an ascending
///    list and floats them to the top of a descending one.
/// 2. The "external ID" column claimed to be sortable and was not - it fell through to the default
///    order, so the arrow in the header drew and the list ignored it.
/// 3. Paging over a tied sort key walks the whole set. Which of the two halves of this file can prove
///    what is noted on the tests themselves: the null-ordering half only holds on Postgres, and
///    lives in the integration suite.
/// </summary>
public sealed class AdminVoucherListSortTests : IDisposable
{
    private static readonly Guid AlphaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BetaId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ApplicationDbContext _context;

    public AdminVoucherListSortTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);

        _context.Suppliers.AddRange(
            new Supplier { Id = AlphaId, Name = "Альфа Палив", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new Supplier { Id = BetaId, Name = "Бета Палив", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });

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

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private FuelVoucher Voucher(string number, Guid? supplierId, decimal? costPerLiter) => new()
    {
        Id = Guid.NewGuid(),
        QrPayload = "qr",
        Liters = 5,
        FuelTypeId = "okko-95",
        Brand = "OKKO",
        SupplierExpirationDate = new DateOnly(2027, 1, 31),
        CustomerExpirationDate = new DateOnly(2027, 1, 31),
        VoucherNumber = number,
        Status = VoucherStatus.Available,
        CreatedAtUtc = DateTime.UtcNow,
        SupplierId = supplierId,
        CostPerLiter = costPerLiter
    };

    private async Task<List<AdminVoucherListItemDto>> SortedAsync(string sortBy, string sortDirection = "asc", int page = 1, int limit = 50)
    {
        var handler = new GetAdminVouchersQueryHandler(_context);
        var response = await handler.HandleAsync(new GetAdminVouchersQuery(page, limit, sortBy, sortDirection));
        return response.Data;
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task Sorting_by_supplier_puts_the_unnamed_last(string direction)
    {
        _context.FuelVouchers.AddRange(
            Voucher("VC-1", AlphaId, 50m),
            Voucher("VC-2", BetaId, 50m),
            Voucher("VC-3", null, null));
        await _context.SaveChangesAsync();

        var rows = await SortedAsync("supplier", direction);

        rows.Should().HaveCount(3);
        rows.Take(2).Select(r => r.SupplierName).Should()
            .BeEquivalentTo(new[] { "Альфа Палив", "Бета Палив" },
                "the direction decides the order of the two known suppliers and neither of them outranks the unknown");
        rows[2].SupplierName.Should().BeNull("a voucher with no supplier sorts last whichever way the column is read");
    }

    [Fact]
    public async Task Sorting_by_supplier_desc_reverses_the_named_suppliers_but_still_leaves_the_unnamed_last()
    {
        _context.FuelVouchers.AddRange(
            Voucher("VC-1", AlphaId, 50m),
            Voucher("VC-2", BetaId, 50m),
            Voucher("VC-3", null, null));
        await _context.SaveChangesAsync();

        var rows = await SortedAsync("supplier", "desc");

        rows.Select(r => r.SupplierName).Should().ContainInOrder("Бета Палив", "Альфа Палив", null);
    }

    [Fact]
    public async Task Sorting_by_cost_ascending_reads_cheapest_first()
    {
        _context.FuelVouchers.AddRange(
            Voucher("VC-1", AlphaId, 94.90m),
            Voucher("VC-2", AlphaId, 50m));
        await _context.SaveChangesAsync();

        var rows = await SortedAsync("costPerLiter", "asc");

        rows.Select(r => r.CostPerLiter).Should().ContainInOrder(50m, 94.90m);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task An_unentered_cost_sorts_last_whichever_way_the_column_is_read(string direction)
    {
        // The regression worth naming: a descending sort on a nullable column puts NULLs first on
        // Postgres, which would put the un-priced vouchers at the top of "most expensive first" -
        // the rows an operator most needs to price, presented as though they were the dearest.
        _context.FuelVouchers.AddRange(
            Voucher("VC-1", AlphaId, 94.90m),
            Voucher("VC-2", AlphaId, null));
        await _context.SaveChangesAsync();

        var rows = await SortedAsync("costPerLiter", direction);

        rows.Should().HaveCount(2);
        rows[0].CostPerLiter.Should().Be(94.90m);
        rows[1].CostPerLiter.Should().BeNull("an unknown cost is not the cheapest cost");
    }

    [Fact]
    public async Task The_column_the_admin_labels_external_id_actually_sorts_by_the_number_it_shows()
    {
        _context.FuelVouchers.AddRange(
            Voucher("VC-300", AlphaId, 50m),
            Voucher("VC-100", AlphaId, 50m),
            Voucher("VC-200", AlphaId, 50m));
        await _context.SaveChangesAsync();

        var rows = await SortedAsync("externalId", "asc");

        rows.Select(r => r.VoucherNumber).Should().ContainInOrder("VC-100", "VC-200", "VC-300");
    }

    [Fact]
    public async Task Paging_over_a_repeated_sort_key_still_walks_every_voucher_exactly_once()
    {
        // The tie-break on Id, seen from the outside. Every voucher here shares one supplier, so the
        // sort key ties across the whole result set and the page boundary is decided by the
        // tie-break alone.
        //
        // Seeded in strictly descending Id order on purpose: LINQ's sort is stable, so with no
        // tie-break the rows keep the order the store hands them out, which is the order they were
        // added. Requiring the opposite order therefore fails without the fix and passes with it -
        // a test that cannot tell the two apart would be worse than none.
        var seeded = Enumerable.Range(0, 9)
            .Select(i => Voucher($"VC-{i:00}", AlphaId, 50m))
            .OrderByDescending(v => v.Id)
            .ToList();

        _context.FuelVouchers.AddRange(seeded);
        await _context.SaveChangesAsync();

        var first = await SortedAsync("supplier", "asc", page: 1, limit: 5);
        var second = await SortedAsync("supplier", "asc", page: 2, limit: 5);

        first.Should().HaveCount(5);
        second.Should().HaveCount(4);

        var walked = first.Concat(second).Select(r => r.Id).ToList();
        walked.Should().HaveCount(9).And.OnlyHaveUniqueItems();
        walked.Should().BeInAscendingOrder("a tied page boundary has to come from the tie-break, not from whatever order the store returned the rows in");
    }
}