using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>is_test_data boolean</c> became <c>data_kind varchar NOT NULL CHECK (...)</c> on the tables
/// that carry a pool. See <see cref="DataKinds"/> for the two production incidents that motivated it.
/// <para>
/// The migration is hand-written because EF's scaffold was destructive. It emitted, in this order:
/// <c>ADD COLUMN data_kind NOT NULL DEFAULT ''</c>, then <c>DROP COLUMN is_test_data</c>, then
/// <c>UpdateData</c> setting <c>data_kind = 'real'</c> for the seeded rows and nothing else. Every row
/// the seed does not know about - which is every voucher created at runtime, every size a fuel is
/// sold in - would have landed on <c>''</c> and been rejected by the very CHECK the same migration
/// adds.
/// </para>
/// <para>
/// <b>The shape of these tests is the point.</b> They do not assert on the seeded catalogue, because
/// the seeded catalogue is exactly the part the broken scaffold got right. Each one inserts a row
/// that <c>SeedData</c> has never heard of, so the assertion fails if the copy step is ever dropped,
/// reordered after the DROP, or narrowed to the seed.
/// </para>
/// <para>
/// <b>These tests ran once.</b> The file was written to disk without a newline in it, so the first
/// <c>//</c> in it commented out the rest - class, tests and all - and the suite stayed green for
/// three weeks while none of this was checked. The shape of the file is now guarded
/// (<c>TestFileShapeTests</c>). The tables named here are the ones that carry a pool today: the
/// vouchers, and the sizes - the pool moved to the size when the offers were dropped on 2026-10-19.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class DataKindMigrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public DataKindMigrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Theory]
    [InlineData("fuel_vouchers")]
    [InlineData("fuel_nominals")]
    public async Task TheBooleanIsGone_ReplacedByANamedColumn(string table)
    {
        await using var context = CreateContext();

        var columns = await context.Database.SqlQueryRaw<string>(
            $"""
             SELECT column_name AS "Value"
             FROM information_schema.columns
             WHERE table_name = '{table}' AND column_name IN ('data_kind', 'is_test_data')
             """).ToListAsync();

        columns.Should().BeEquivalentTo(["data_kind"],
            "the named value replaced the boolean rather than sitting beside it");
    }

    [Theory]
    [InlineData("fuel_vouchers")]
    [InlineData("fuel_nominals")]
    public async Task TheColumnIsNotNull_AndPinnedToTheKnownSet(string table)
    {
        await using var context = CreateContext();

        // SqlQueryRaw, not ExecuteSql*: ExecuteSql returns rows-affected (-1 for a SELECT), which
        // compares equal to nothing and would have passed against any row count.
        var nulls = await context.Database.SqlQueryRaw<int>(
            $"SELECT count(*) FROM {table} WHERE data_kind IS NULL").ToListAsync();

        nulls.Should().ContainSingle().Which.Should().Be(0);

        // NOT NULL + CHECK is the whole point of the rename, so assert the constraint exists by
        // name rather than trusting the migration ran - an absent CHECK is a silent regression.
        var constraints = await context.Database.SqlQueryRaw<string>(
            $"""
             SELECT con.conname AS "Value"
             FROM pg_constraint con
             JOIN pg_class rel ON rel.oid = con.conrelid
             WHERE rel.relname = '{table}'
               AND con.conname = 'ck_{table}_data_kind'
             """).ToListAsync();

        constraints.Should().ContainSingle(
            "a name is only worth having if the database pins the set");
    }

    [Fact]
    public async Task ARowTheSeedDoesNotKnow_StillGetsAKind()
    {
        // The regression pin. Under the scaffolded ordering this insert would have failed on the
        // CHECK with data_kind = '', and the failure would have appeared only on a database that
        // actually holds stock.
        var (fuelId, brandSlug) = await NewFuelAsync();
        var size = NewSize(fuelId);
        var voucher = NewVoucher(fuelId, brandSlug);

        await using (var context = CreateContext())
        {
            context.FuelNominals.Add(size);
            context.FuelVouchers.Add(voucher);
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();

        var storedSize = await verify.FuelNominals.AsNoTracking().SingleAsync(n => n.FuelTypeId == fuelId);
        var storedVoucher = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucher.Id);

        storedSize.DataKind.Should().Be(DataKinds.Real, "real unless somebody says otherwise");
        storedVoucher.DataKind.Should().Be(DataKinds.Real);
    }

    [Fact]
    public async Task TheQaFlag_RoundTrips_ThroughTheColumn()
    {
        var (fuelId, brandSlug) = await NewFuelAsync();
        var size = NewSize(fuelId, isTestData: true);
        var voucher = NewVoucher(fuelId, brandSlug, isTestData: true);

        await using (var context = CreateContext())
        {
            context.FuelNominals.Add(size);
            context.FuelVouchers.Add(voucher);
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();

        var storedSize = await verify.FuelNominals.AsNoTracking().SingleAsync(n => n.FuelTypeId == fuelId);
        var storedVoucher = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucher.Id);

        storedSize.DataKind.Should().Be(DataKinds.Qa);
        storedVoucher.DataKind.Should().Be(DataKinds.Qa);

        // And the shim still reads correctly off a freshly materialised row, because that is how the
        // admin DTOs are filled.
        storedVoucher.IsTestData.Should().BeTrue();

        // What a cleanup and the admin list filter on. Both arms compare the mapped column: the
        // IsTestData shim is [NotMapped] and cannot be translated, so the boolean form of this
        // predicate throws "could not be translated" at run time rather than failing to compile.
        (await verify.FuelVouchers.CountAsync(v => v.DataKind == DataKinds.Qa && v.Id == voucher.Id))
            .Should().Be(1);
        (await verify.FuelVouchers.CountAsync(v => v.DataKind == DataKinds.Real && v.Id == voucher.Id))
            .Should().Be(0);
    }

    [Fact]
    public async Task AnUnknownKind_IsRefusedByTheDatabase()
    {
        await using var context = CreateContext();

        var (sandboxFuelId, sandboxSlug) = await NewFuelAsync();
        context.FuelVouchers.Add(NewVoucher(sandboxFuelId, sandboxSlug, dataKind: "sandbox"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "the CHECK is what stops the next unrehearsed rehearsal row from being sellable");
    }

    [Fact]
    public void TheShim_MapsBothWays_InMemory()
    {
        // The C# surface stays a bool, so every caller that asks "is this the QA pool?" reads the
        // same way - only the storage underneath changed.
        var size = new FuelNominal();
        var voucher = new FuelVoucher();

        size.DataKind.Should().Be(DataKinds.Real);
        voucher.DataKind.Should().Be(DataKinds.Real);

        voucher.IsTestData = true;
        voucher.DataKind.Should().Be(DataKinds.Qa, "assigning the shim is how the write paths state intent");

        voucher.IsTestData = false;
        voucher.DataKind.Should().Be(DataKinds.Real);
    }

    [Theory]
    [InlineData("real", true)]
    [InlineData("qa", true)]
    [InlineData("sandbox", false)]
    [InlineData("QA", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsKnown_AcceptsOnlyTheTwoPools(string? kind, bool expected)
    {
        // The set is case-sensitive on purpose: the column is written by two constants, and a
        // case-insensitive check would imply a looser contract than the database enforces.
        DataKinds.IsKnown(kind).Should().Be(expected);
    }

    /// <summary>
    /// A fuel nobody seeded, with the brand it belongs to. The size hangs off it: the pool lives on
    /// the size, and a size cannot exist without its fuel.
    /// </summary>
    private async Task<(Guid FuelTypeId, string BrandSlug)> NewFuelAsync()
    {
        await using var context = CreateContext();

        // The slug is unique, so two tests in this class - which share one database - do not
        // collide on it.
        var slug = "dk-" + Guid.NewGuid().ToString("N")[..8];

        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Name = slug,
            Color = "#000",
            LogoText = slug,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        context.Brands.Add(brand);

        var fuel = new FuelTypeEntity
        {
            Id = Guid.NewGuid(),
            BrandId = brand.Id,
            Name = slug + " fuel",
            BasePrice = 50m,
            DiscountPrice = 50m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        context.FuelTypes.Add(fuel);

        await context.SaveChangesAsync();
        return (fuel.Id, slug);
    }

    /// <summary>
    /// A size of a fuel - the row that carries the pool now, since the offers were dropped on
    /// 2026-10-19 and the pool moved onto the size with them.
    /// </summary>
    private static FuelNominal NewSize(Guid fuelTypeId, bool isTestData = false, string? dataKind = null)
    {
        var size = new FuelNominal
        {
            FuelTypeId = fuelTypeId,
            Liters = 10m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        if (isTestData) size.DataKind = DataKinds.Qa;
        if (dataKind is not null) size.DataKind = dataKind;

        return size;
    }

    private static FuelVoucher NewVoucher(Guid fuelTypeId, string brandSlug, bool isTestData = false, string? dataKind = null)
    {
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3));

        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            // The voucher's network is the brand's slug, and the database refuses a row that names
            // a different network than the fuel it claims.
            Brand = brandSlug,
            FuelTypeId = fuelTypeId,
            Liters = 10m,
            SupplierExpirationDate = expiry,
            CustomerExpirationDate = expiry,
            VoucherNumber = $"dk-{Guid.NewGuid():N}",
            QrPayload = $"dk-qr-{Guid.NewGuid():N}",
            Status = VoucherStatus.Available,
            IsTestData = isTestData,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        if (dataKind is not null)
            voucher.DataKind = dataKind;

        return voucher;
    }
}
