using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.UnitTests.Persistence;

/// <summary>
/// Pins what each named scope lifts. A raw <c>IgnoreQueryFilters()</c> lifts every filter on every
/// entity in the query, including one added years later; these helpers name the one they mean, and
/// this file is what stops a future edit from widening one of them back into a blanket bypass.
/// </summary>
public sealed class QueryFilterScopesTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public QueryFilterScopesTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);

        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("okko-95"),
            Name = "A-95",
            BrandId = BrandIds.Okko,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        _context.SaveChanges();
    }

    [Fact]
    public void The_soft_delete_scope_lifts_only_soft_delete()
    {
        _context.FuelVouchers.AddRange(
            NewVoucher(isDeleted: false),
            NewVoucher(isDeleted: true));
        _context.Fulfillments.AddRange(
            NewFulfillment(releasedAtUtc: null),
            NewFulfillment(releasedAtUtc: TestClock.UtcNow));
        _context.SaveChanges();

        _context.FuelVouchers.Count().Should().Be(1, "the model hides the deleted voucher");
        _context.FuelVouchers.IncludingSoftDeleted().Count().Should().Be(2);
        _context.FuelVouchers.IncludingReleasedFulfilments().Count()
            .Should().Be(1, "the fulfilment scope must not lift a filter it does not name");
        _context.FuelVouchers.IncludingSoftDeletedAndReleasedFulfilments().Count().Should().Be(2);
    }

    [Fact]
    public void The_released_fulfilment_scope_lifts_only_released_fulfilments()
    {
        _context.FuelVouchers.AddRange(
            NewVoucher(isDeleted: false),
            NewVoucher(isDeleted: true));
        _context.Fulfillments.AddRange(
            NewFulfillment(releasedAtUtc: null),
            NewFulfillment(releasedAtUtc: TestClock.UtcNow));
        _context.SaveChanges();

        _context.Fulfillments.Count().Should().Be(1, "the model hides the released delivery");
        _context.Fulfillments.IncludingReleasedFulfilments().Count().Should().Be(2);
        _context.Fulfillments.IncludingSoftDeleted().Count()
            .Should().Be(1, "the soft-delete scope must not lift a filter it does not name");
        _context.Fulfillments.IncludingSoftDeletedAndReleasedFulfilments().Count().Should().Be(2);
    }

    [Fact]
    public void The_combined_scope_lifts_both_and_nothing_else()
    {
        _context.FuelVouchers.Add(NewVoucher(isDeleted: true));
        _context.Fulfillments.Add(NewFulfillment(releasedAtUtc: TestClock.UtcNow));
        _context.SaveChanges();

        _context.FuelVouchers.IncludingSoftDeletedAndReleasedFulfilments().Count().Should().Be(1);
        _context.Fulfillments.IncludingSoftDeletedAndReleasedFulfilments().Count().Should().Be(1);
    }

    private static FuelVoucher NewVoucher(bool isDeleted) => new()
    {
        Id = Guid.NewGuid(),
        BrandId = BrandIds.Okko,
        Brand = "okko",
        FuelTypeId = ToUuid("okko-95"),
        Liters = 50m,
        SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
        CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
        VoucherNumber = $"OKKO-{Guid.NewGuid():N}"[..20],
        QrPayload = Guid.NewGuid().ToString(),
        Status = VoucherStatus.Available,
        IsDeleted = isDeleted,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    private static Fulfillment NewFulfillment(DateTime? releasedAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        VoucherId = Guid.NewGuid(),
        FulfilledAtUtc = TestClock.UtcNow,
        ReleasedAtUtc = releasedAtUtc
    };

    public void Dispose() => _context.Dispose();
}
