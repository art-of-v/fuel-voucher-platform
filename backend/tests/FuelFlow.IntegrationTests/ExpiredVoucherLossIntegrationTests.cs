using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Covers the nightly ExpiredVoucherLossService (pricing slice 4) against a real PostgreSQL container:
/// when enabled it retires only operator-owned, still-sellable stock whose printed expiration date has
/// passed to <see cref="VoucherStatus.Expired"/>, leaving future-dated stock, already-sold (Assigned/Used)
/// vouchers and same-day expiries untouched; when disabled it is a pure dry-run that mutates nothing.
/// </summary>
[Collection("Integration Tests")]
public sealed class ExpiredVoucherLossIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private static readonly Guid ImportId = Guid.NewGuid();

    public ExpiredVoucherLossIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BookExpiredLoss_WhenEnabled_RetiresOnlyLapsedUnsoldInStock()
    {
        await SeedAsync(seed =>
        {
            Enable(seed);
            SeedFixture(seed);
        });

        await RunAsync();

        using var verify = CreateContext();
        // The three lapsed, still-sellable vouchers are the only ones retired.
        (await StatusOf(verify, "instock-imported")).Should().Be(VoucherStatus.Expired);
        (await StatusOf(verify, "instock-available")).Should().Be(VoucherStatus.Expired);
        (await StatusOf(verify, "instock-warnings")).Should().Be(VoucherStatus.Expired);

        // Everything else is left exactly as seeded.
        (await StatusOf(verify, "future-instock")).Should().Be(VoucherStatus.Imported);
        (await StatusOf(verify, "today-instock")).Should().Be(VoucherStatus.Imported);
        (await StatusOf(verify, "lapsed-assigned")).Should().Be(VoucherStatus.Assigned);
        (await StatusOf(verify, "lapsed-used")).Should().Be(VoucherStatus.Used);

        (await verify.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.Expired)).Should().Be(3);
    }

    [Fact]
    public async Task BookExpiredLoss_WhenDisabled_MutatesNothing()
    {
        await SeedAsync(seed =>
        {
            // ExpiredVoucherLoss:Enabled intentionally NOT set -> fail-safe default of false (dry-run).
            SeedFixture(seed);
        });

        await RunAsync();

        using var verify = CreateContext();
        (await verify.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.Expired)).Should().Be(0);
        (await StatusOf(verify, "instock-imported")).Should().Be(VoucherStatus.Imported);
        (await StatusOf(verify, "instock-available")).Should().Be(VoucherStatus.Available);
        (await StatusOf(verify, "instock-warnings")).Should().Be(VoucherStatus.VerifiedWithWarnings);
    }

    private static async Task<VoucherStatus> StatusOf(ApplicationDbContext context, string number)
        => (await context.FuelVouchers.FirstAsync(v => v.VoucherNumber == number)).Status;

    private static void Enable(ApplicationDbContext seed) => seed.AppSettings.Add(new AppSetting
    {
        Key = AppSettingKeys.ExpiredVoucherLossEnabled,
        Value = "true",
        UpdatedAtUtc = DateTime.UtcNow
    });

    private async Task RunAsync()
    {
        using var context = CreateContext();
        var service = new ExpiredVoucherLossService(
            context, new RuntimeSettingsService(context), NullLogger<ExpiredVoucherLossService>.Instance);
        await service.BookExpiredLossAsync();
    }

    // Vouchers tagged by a stable VoucherNumber discriminator. "instock-*" = operator-owned, sellable and
    // lapsed (must be retired); "future-instock"/"today-instock" = sellable but not past expiry (kept);
    // "lapsed-assigned"/"lapsed-used" = already sold, so the operator loss never applies (kept).
    private static void SeedFixture(ApplicationDbContext seed)
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        // fuel_vouchers.import_job_id is a FK to voucher_imports — seed the parent row first
        // (real Postgres enforces it; the InMemory unit tests do not).
        seed.VoucherImports.Add(new VoucherImport
        {
            Id = ImportId,
            FileName = "batch.pdf",
            Status = "Completed",
            StartedAtUtc = DateTime.UtcNow
        });

        seed.FuelVouchers.Add(NewVoucher("instock-imported", VoucherStatus.Imported, yesterday));
        seed.FuelVouchers.Add(NewVoucher("instock-available", VoucherStatus.Available, yesterday));
        seed.FuelVouchers.Add(NewVoucher("instock-warnings", VoucherStatus.VerifiedWithWarnings, yesterday));
        seed.FuelVouchers.Add(NewVoucher("future-instock", VoucherStatus.Imported, tomorrow));
        seed.FuelVouchers.Add(NewVoucher("today-instock", VoucherStatus.Imported, today));
        seed.FuelVouchers.Add(NewVoucher("lapsed-assigned", VoucherStatus.Assigned, yesterday));
        seed.FuelVouchers.Add(NewVoucher("lapsed-used", VoucherStatus.Used, yesterday));
    }

    private static FuelVoucher NewVoucher(string number, VoucherStatus status, DateOnly expirationDate) => new()
    {
        Id = Guid.NewGuid(),
        Provider = "OKKO",
        FuelTypeId = "okko-dp",
        Liters = 100m,
        ExpirationDate = expirationDate,
        VoucherNumber = number,
        QrPayload = Guid.NewGuid().ToString(),
        Status = status,
        ImportJobId = ImportId,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private async Task SeedAsync(Action<ApplicationDbContext> seed)
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"fuel_vouchers\", \"purchase_batches\", \"voucher_imports\", \"app_settings\" RESTART IDENTITY CASCADE");
        seed(context);
        await context.SaveChangesAsync();
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}
