using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.UnitTests.BackgroundJobs;

/// <summary>
/// The nightly expired-voucher loss-booking job must respect the exchange grace window: a voucher
/// whose printed expiry has passed is not retired until the window has closed, because inside it
/// the operator can still swap the voucher with the brand for a surcharge. Booking the loss at
/// the printed date writes off stock that is still salvageable — and, once the voucher is
/// <c>Expired</c>, the operator→brand exchange (#104) may no longer accept it, so the job would
/// itself have destroyed the option it was too early to close.
///
/// Both switches are read from the <c>app_settings</c> table, which is what the tests seed — the
/// same path production uses, so a mis-wired key would fail here rather than only in prod.
/// </summary>
public sealed class ExpiredVoucherLossServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public ExpiredVoucherLossServiceTests()
    {
        _context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options, TestQrCipher.Instance);
        _context.Database.EnsureCreated();
        SeedLedger();
    }

    /// <summary>
    /// The two accounts and the one mapping rule the write-off needs, seeded the way the
    /// migration seeds them in a real database. The job posts into the ledger, so without them
    /// it would fail with "no debit account is mapped" - which is the point of the engine, but
    /// not what these tests are about.
    /// </summary>
    private void SeedLedger()
    {
        var loss = new ChartOfAccount
        {
            Id = Guid.NewGuid(),
            Code = "807",
            NameUkr = "Витрати від утрати товарів",
            AccountType = AccountType.Expense,
            NormalBalance = NormalBalance.Debit,
            IsPostable = true,
            IsActive = true
        };
        var stock = new ChartOfAccount
        {
            Id = Guid.NewGuid(),
            Code = "281",
            NameUkr = "Товари на складі",
            AccountType = AccountType.Asset,
            NormalBalance = NormalBalance.Debit,
            IsPostable = true,
            IsActive = true
        };
        _context.ChartOfAccounts.AddRange(loss, stock);

        _context.LedgerMappingRules.AddRange(
            new LedgerMappingRule
            {
                Id = Guid.NewGuid(),
                SourceType = JournalSourceType.Cost,
                SourceEvent = "stock.expired.writeoff",
                AccountId = loss.Id,
                Side = NormalBalance.Debit
            },
            new LedgerMappingRule
            {
                Id = Guid.NewGuid(),
                SourceType = JournalSourceType.Cost,
                SourceEvent = "stock.expired.writeoff",
                AccountId = stock.Id,
                Side = NormalBalance.Credit
            });
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private void SeedVoucher(string number, int expiredDaysAgo)
        => _context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-dp"),
            Liters = 10m,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddDays(-expiredDaysAgo)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddDays(-expiredDaysAgo)),
            VoucherNumber = number,
            QrPayload = Guid.NewGuid().ToString(),
            Status = VoucherStatus.Available,
            ImportJobId = Guid.NewGuid(),
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

    /// <summary>
    /// The author of the setting. The journal will not take a posting that names nobody, and a
    /// nightly sweep has nobody of its own - so the write-off is posted under the operator who
    /// turned the sweep on, which is who the admin screen records.
    /// </summary>
    private readonly Guid _operatorId = Guid.NewGuid();

    private void SeedSetting(string key, string value)
        => _context.AppSettings.Add(new AppSetting
        {
            Key = key,
            Value = value,
            UpdatedAtUtc = TestClock.UtcNow,
            UpdatedByUserId = _operatorId,
            UpdatedByUserName = "Test Operator"
        });

    private async Task RunAsync(int graceDays, bool enabled = true)
    {
        SeedSetting(AppSettingKeys.ExpiredVoucherLossEnabled, enabled ? "true" : "false");
        SeedSetting(AppSettingKeys.ExpiredVoucherLossGraceDays, graceDays.ToString());
        await _context.SaveChangesAsync();

        var service = new ExpiredVoucherLossService(
            _context,
            new RuntimeSettingsService(_context),
            new JournalPostingService(_context, NullLogger<JournalPostingService>.Instance),
            NullLogger<ExpiredVoucherLossService>.Instance);

        await service.BookExpiredLossAsync();
    }

    private VoucherStatus StatusOf(string number)
        => _context.FuelVouchers.AsNoTracking().Single(v => v.VoucherNumber == number).Status;

    [Fact]
    public async Task Retires_VoucherOlderThanTheGraceWindow()
    {
        // Expired 15 days ago against a 14-day window: the window has closed, so the loss is real.
        SeedVoucher("EXPIRED-001", expiredDaysAgo: 15);

        await RunAsync(graceDays: 14);

        StatusOf("EXPIRED-001").Should().Be(VoucherStatus.Expired);
    }

    [Fact]
    public async Task Keeps_VoucherStillInsideTheGraceWindow()
    {
        // Expired 3 days ago against a 14-day window: the operator can still exchange it.
        SeedVoucher("GRACE-001", expiredDaysAgo: 3);

        await RunAsync(graceDays: 14);

        StatusOf("GRACE-001").Should().Be(VoucherStatus.Available);
    }

    // The boundary decides whether stock is written off a day early, so it is pinned rather than
    // left to whichever side `<` happens to fall on.
    [Fact]
    public async Task Keeps_VoucherExactlyOnTheLastDayOfTheWindow()
    {
        SeedVoucher("BOUNDARY-IN", expiredDaysAgo: 14);

        await RunAsync(graceDays: 14);

        StatusOf("BOUNDARY-IN").Should().Be(VoucherStatus.Available);
    }

    [Fact]
    public async Task Retires_VoucherTheDayAfterTheWindowCloses()
    {
        SeedVoucher("BOUNDARY-OUT", expiredDaysAgo: 15);

        await RunAsync(graceDays: 14);

        StatusOf("BOUNDARY-OUT").Should().Be(VoucherStatus.Expired);
    }

    [Fact]
    public async Task GraceDaysZero_BooksOnThePrintedDate_LegacyBehaviour()
    {
        // 0 is the opt-out: it restores the pre-grace behaviour exactly, so an operator who wants
        // the old accounting is not forced into an arbitrary window.
        SeedVoucher("LEGACY-001", expiredDaysAgo: 1);

        await RunAsync(graceDays: 0);

        StatusOf("LEGACY-001").Should().Be(VoucherStatus.Expired);
    }

    [Fact]
    public async Task SplitsAMixedBatch_ByTheWindow()
    {
        // The case that matters in production: a sweep with both kinds present.
        SeedVoucher("GRACE-IN", expiredDaysAgo: 5);
        SeedVoucher("GRACE-OUT", expiredDaysAgo: 20);

        await RunAsync(graceDays: 14);

        StatusOf("GRACE-IN").Should().Be(VoucherStatus.Available);
        StatusOf("GRACE-OUT").Should().Be(VoucherStatus.Expired);
    }

    // A missing row must not be read as 0: that would silently restore the pre-grace behaviour on an
    // environment that never configured the key, which is the bug this issue is about.
    [Fact]
    public async Task MissingGraceDaysRow_FallsBackToTheDefaultWindow_NotZero()
    {
        SeedVoucher("UNSET-001", expiredDaysAgo: 3);

        await _context.SaveChangesAsync();
        SeedSetting(AppSettingKeys.ExpiredVoucherLossEnabled, "true");
        await _context.SaveChangesAsync();

        var service = new ExpiredVoucherLossService(
            _context,
            new RuntimeSettingsService(_context),
            new JournalPostingService(_context, NullLogger<JournalPostingService>.Instance),
            NullLogger<ExpiredVoucherLossService>.Instance);
        await service.BookExpiredLossAsync();

        StatusOf("UNSET-001").Should().Be(VoucherStatus.Available,
            "the default window applies when the key is absent, not 'no window'");
    }

    [Fact]
    public async Task Disabled_StillRetiresNothing()
    {
        SeedVoucher("OFF-001", expiredDaysAgo: 400);

        await RunAsync(graceDays: 14, enabled: false);

        StatusOf("OFF-001").Should().Be(VoucherStatus.Available,
            "the fail-safe dry run is unchanged by the grace window");
    }
}