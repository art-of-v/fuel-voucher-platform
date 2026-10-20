using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// A voucher's number and its QR payload are unique - but only among <em>live</em> vouchers.
/// <para>
/// The two code paths used to disagree. The import's duplicate check reads through the
/// <c>!IsDeleted</c> query filter, so an archived voucher looked free and the import proceeded to
/// insert - where a DB-global unique index refused it. The operator saw an uninterpretable failure
/// on a paper they had merely archived by mistake.
/// </para>
/// <para>
/// #1067 made soft delete the only delete path, so archived rows never disappear and the keys they
/// squat on would have been held forever. These tests pin the release, and the guarantee that
/// matters more: the same paper cannot be <em>live</em> twice under two numbers.
/// </para>
/// <para>
/// Integration, not unit: the filter lives in a partial unique index, and the in-memory brand has
/// no indexes at all - a unit test here would pass whether or not the database enforces anything.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class ArchivedVoucherKeysIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ArchivedVoucherKeysIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task An_Archived_Vouchers_Number_And_Qr_Can_Be_Imported_Again()
    {
        using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        var number = $"reuse-{Guid.NewGuid():N}";
        var qr = $"reuse-qr-{Guid.NewGuid():N}";

        var archived = Voucher(number, qr);
        archived.Status = VoucherStatus.Available;
        ctx.FuelVouchers.Add(archived);
        await ctx.SaveChangesAsync();

        // Archive it the way the app does - a flag, never a delete. FuelVoucher carries no
        // deleted-at column; IsDeleted is the whole of it.
        archived.IsDeleted = true;
        await ctx.SaveChangesAsync();

        // The import's duplicate check reads through the filter, so this is what it sees.
        var visible = await ctx.FuelVouchers
            .AnyAsync(v => v.VoucherNumber == number || v.QrPayload == qr);
        visible.Should().BeFalse("an archived voucher is invisible to the duplicate check");

        // And the insert that follows it used to die here on the unique index.
        var reimported = Voucher(number, qr);
        var act = async () =>
        {
            ctx.FuelVouchers.Add(reimported);
            await ctx.SaveChangesAsync();
        };
        await act.Should().NotThrowAsync("archiving must release the keys it was squatting on");

        // Both rows survive: the archive is history, the new one is the live paper.
        var stored = await ctx.FuelVouchers.IgnoreQueryFilters()
            .Where(v => v.VoucherNumber == number)
            .ToListAsync();
        stored.Should().HaveCount(2);
        stored.Count(v => v.IsDeleted).Should().Be(1);
        stored.Count(v => !v.IsDeleted).Should().Be(1);
    }

    [Fact]
    public async Task The_Same_Paper_Cannot_Be_Live_Twice()
    {
        // The guarantee the filter must not cost us: dedup among live vouchers is a security
        // control, not a convenience - the same paper imported under two numbers would fulfil twice.
        using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        var number = $"dup-{Guid.NewGuid():N}";
        var qr = $"dup-qr-{Guid.NewGuid():N}";

        ctx.FuelVouchers.Add(Voucher(number, qr));
        await ctx.SaveChangesAsync();

        var clash = Voucher(number, qr);
        var act = async () =>
        {
            ctx.FuelVouchers.Add(clash);
            await ctx.SaveChangesAsync();
        };
        await act.Should().ThrowAsync<DbUpdateException>(
            "two live vouchers sharing a number is exactly what this index exists to stop");
    }

    private static FuelVoucher Voucher(string number, string qr) => new()
    {
        Id = Guid.NewGuid(),
        Brand = "OKKO",
        FuelTypeId = ToUuid("okko-95"),
        Liters = 10m,
        SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
        CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
        VoucherNumber = number,
        QrPayload = qr,
        Status = VoucherStatus.Available,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private ApplicationDbContext CreateContext()
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .Options,
            TestQrCipher.Instance
        );
}
