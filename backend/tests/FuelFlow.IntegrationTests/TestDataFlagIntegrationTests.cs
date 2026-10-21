using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>data_kind = 'qa'</c> marks a voucher as emulated: a seeded row whose code does not exist at the
/// Brand. It is the thing that lets a QA account be handed test fuel without that fuel ever being
/// sellable to a paying customer, and it is the thing a cleanup runs on.
/// <para>
/// The default matters most of all. It is <see cref="DataKinds.Real"/>, so every voucher that
/// already existed and every voucher a supplier import ever creates is real fuel unless somebody
/// deliberately says otherwise — a seed that forgets the kind is visible as a bug, whereas a default
/// of QA would quietly mark real fuel as fake. See <c>EnforceDataKind</c> for the migration and
/// <c>DataKindMigrationTests</c> for why the value is named and pinned.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class TestDataFlagIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public TestDataFlagIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_Voucher_Is_Real_Fuel_Unless_Somebody_Flags_It()
    {
        using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 10m,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(3)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(3)),
            VoucherNumber = $"flag-default-{Guid.NewGuid():N}",
            QrPayload = $"flag-qr-{Guid.NewGuid():N}",
            Status = VoucherStatus.Available,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

        ctx.FuelVouchers.Add(voucher);
        await ctx.SaveChangesAsync();

        var stored = await ctx.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucher.Id);
        stored.IsTestData.Should().BeFalse();
    }

    [Fact]
    public async Task The_Flag_Survives_The_Round_Trip_And_Is_Filterable()
    {
        var flagged = Guid.NewGuid();
        var plain = Guid.NewGuid();

        using (var ctx = CreateContext())
        {
            await ctx.Database.MigrateAsync();
            ctx.FuelVouchers.Add(Voucher(flagged, "flagged", isTestData: true));
            ctx.FuelVouchers.Add(Voucher(plain, "plain", isTestData: false));
            await ctx.SaveChangesAsync();
        }

        using var verify = CreateContext();
        var asStored = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == flagged);
        asStored.IsTestData.Should().BeTrue();

        // What a cleanup and the admin list both filter on. Against the mapped column, not the
        // IsTestData shim: the shim is [NotMapped] and EF cannot translate a getter on an unmapped
        // property, so the boolean predicate throws only once the query is actually executed.
        (await verify.FuelVouchers.AsNoTracking().CountAsync(v => v.DataKind == DataKinds.Qa && v.Id == flagged))
            .Should().Be(1);
        (await verify.FuelVouchers.AsNoTracking().CountAsync(v => v.DataKind == DataKinds.Real && v.Id == plain))
            .Should().Be(1);
    }

    private static FuelVoucher Voucher(Guid id, string tag, bool isTestData)
        => new()
        {
            Id = id,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 10m,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(3)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(3)),
            VoucherNumber = $"{tag}-{id:N}",
            QrPayload = $"{tag}-qr-{id:N}",
            Status = VoucherStatus.Available,
            IsTestData = isTestData,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

    private ApplicationDbContext CreateContext()
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .Options,
            TestQrCipher.Instance
        );
}