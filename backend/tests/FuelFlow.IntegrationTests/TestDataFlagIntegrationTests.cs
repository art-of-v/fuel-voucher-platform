using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>is_test_data</c> marks a voucher as emulated: a seeded row whose code does not exist at the
/// station. It is the thing that lets a QA account be handed test fuel without that fuel ever being
/// sellable to a paying customer, and it is the thing a cleanup runs on.
///
/// The default matters most of all. It is <c>false</c>, so every voucher that already existed and
/// every voucher a supplier import ever creates is real fuel unless somebody deliberately says
/// otherwise — a seed that forgets the flag is visible as a bug, whereas a flag that defaulted to
/// true would quietly mark real fuel as fake.
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
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 10m,
            ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            VoucherNumber = $"flag-default-{Guid.NewGuid():N}",
            QrPayload = $"flag-qr-{Guid.NewGuid():N}",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
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

        // What a cleanup and the admin list both filter on.
        (await verify.FuelVouchers.AsNoTracking().CountAsync(v => v.IsTestData && v.Id == flagged))
            .Should().Be(1);
        (await verify.FuelVouchers.AsNoTracking().CountAsync(v => !v.IsTestData && v.Id == plain))
            .Should().Be(1);
    }

    private static FuelVoucher Voucher(Guid id, string tag, bool isTestData)
        => new()
        {
            Id = id,
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 10m,
            ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            VoucherNumber = $"{tag}-{id:N}",
            QrPayload = $"{tag}-qr-{id:N}",
            Status = VoucherStatus.Available,
            IsTestData = isTestData,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private ApplicationDbContext CreateContext()
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(_fixture.DbContainer.GetConnectionString())
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .Options
        );
}