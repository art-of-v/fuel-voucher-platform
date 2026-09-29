using FluentAssertions;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Renewal.Checkout;
using FuelFlow.Features.Vouchers.Renewal.Quote;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Vouchers.Renewal;

/// <summary>
/// Unit coverage for the renewal quote handler (Slice 4): the read-only preview the mobile term
/// picker leans on. Asserts branch resolution, per-tier pricing, and — crucially — the availability
/// flags that let the client disable a tier («тимчасово недоступно») before any payment: offerable
/// config, extend-vs-replace, and replace-branch stock reach per term. Nothing here mutates a voucher.
/// </summary>
public sealed class RenewalQuoteCommandHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RenewalQuoteCommandHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public RenewalQuoteCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _context = new ApplicationDbContext(options);
        SeedActiveUser();

        _handler = new RenewalQuoteCommandHandler(_context, new RuntimeSettingsService(_context));
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task Quote_ExtendBranch_AllOfferableTiersAvailableWithLinearPrice()
    {
        EnableRenewal(("1w", 5m), ("1m", 10m));
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(5), status: VoucherStatus.Assigned);

        var response = await _handler.HandleAsync(Command(source.Id));

        response.Enabled.Should().BeTrue();
        response.ThresholdDays.Should().Be(14); // default

        var quote = response.Vouchers.Should().ContainSingle().Subject;
        quote.Eligible.Should().BeTrue();
        quote.Branch.Should().Be("extend");
        quote.Terms.Should().HaveCount(8); // full ladder always returned

        var oneWeek = quote.Terms.Single(t => t.Term == "1w");
        oneWeek.Available.Should().BeTrue();
        oneWeek.PriceUah.Should().Be(250); // 50 L × 5

        var oneMonth = quote.Terms.Single(t => t.Term == "1m");
        oneMonth.Available.Should().BeTrue();
        oneMonth.PriceUah.Should().Be(500); // 50 L × 10

        // A tier the manager never priced comes back disabled with a reason (extend needs no stock).
        var twoWeeks = quote.Terms.Single(t => t.Term == "2w");
        twoWeeks.Available.Should().BeFalse();
        twoWeeks.UnavailableReason.Should().Be("not_offerable");
    }

    [Fact]
    public async Task Quote_ReplaceBranch_TiersGatedByStockExpiry()
    {
        EnableRenewal(("1m", 10m), ("6m", 30m));
        var source = SeedVoucher(liters: 40m, expiry: Today.AddDays(-3), status: VoucherStatus.Expired);
        // Stock reaches ~3 months out: enough for 1m, not for 6m.
        SeedStock(source, expiry: Today.AddMonths(3));

        var response = await _handler.HandleAsync(Command(source.Id));

        var quote = response.Vouchers.Should().ContainSingle().Subject;
        quote.Branch.Should().Be("replace");

        var oneMonth = quote.Terms.Single(t => t.Term == "1m");
        oneMonth.Available.Should().BeTrue();
        oneMonth.PriceUah.Should().Be(400); // 40 L × 10

        var sixMonths = quote.Terms.Single(t => t.Term == "6m");
        sixMonths.Available.Should().BeFalse();
        sixMonths.UnavailableReason.Should().Be("no_stock"); // offerable, but no stock reaches +6m
    }

    [Fact]
    public async Task Quote_ReplaceBranch_NoStockAtAll_AllReplaceTiersNoStock()
    {
        EnableRenewal(("1m", 10m));
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(-1), status: VoucherStatus.Expired);
        // No stock seeded.

        var response = await _handler.HandleAsync(Command(source.Id));

        var quote = response.Vouchers.Should().ContainSingle().Subject;
        quote.Branch.Should().Be("replace");
        quote.Terms.Single(t => t.Term == "1m").UnavailableReason.Should().Be("no_stock");
    }

    [Fact]
    public async Task Quote_VoucherStillValidBeyondWindow_NotRenewable()
    {
        EnableRenewal(("1m", 10m));
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(60), status: VoucherStatus.Assigned);

        var response = await _handler.HandleAsync(Command(source.Id));

        var quote = response.Vouchers.Should().ContainSingle().Subject;
        quote.Eligible.Should().BeFalse();
        quote.IneligibleReason.Should().Be("not_renewable");
        quote.Terms.Should().BeEmpty();
    }

    [Fact]
    public async Task Quote_VoucherNotOwnedByCaller_NotYourVoucher()
    {
        EnableRenewal(("1m", 10m));
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(5), status: VoucherStatus.Assigned, ownerId: Guid.NewGuid());

        var response = await _handler.HandleAsync(Command(source.Id));

        var quote = response.Vouchers.Should().ContainSingle().Subject;
        quote.Eligible.Should().BeFalse();
        quote.IneligibleReason.Should().Be("not_your_voucher");
    }

    [Fact]
    public async Task Quote_FeatureDisabled_ReturnsDisabledAndNoVouchers()
    {
        // No config seeded → fail-safe off.
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(5), status: VoucherStatus.Assigned);

        var response = await _handler.HandleAsync(Command(source.Id));

        response.Enabled.Should().BeFalse();
        response.Vouchers.Should().BeEmpty();
    }

    [Fact]
    public async Task Quote_EmptyBatch_ReturnsEnabledWithNoVouchers()
    {
        EnableRenewal(("1m", 10m));

        var response = await _handler.HandleAsync(new RenewalQuoteCommand { UserId = _userId });

        response.Enabled.Should().BeTrue();
        response.Vouchers.Should().BeEmpty();
    }

    [Fact]
    public async Task Quote_TooManyItems_Throws()
    {
        EnableRenewal(("1m", 10m));
        var command = new RenewalQuoteCommand { UserId = _userId };
        for (var i = 0; i < 51; i++)
            command.VoucherIds.Add(Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<VoucherRenewalException>(() => _handler.HandleAsync(command));
        ex.Code.Should().Be("too_many_items");
    }

    [Fact]
    public async Task Quote_MissingUserId_Throws()
    {
        EnableRenewal(("1m", 10m));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.HandleAsync(new RenewalQuoteCommand { UserId = null, VoucherIds = { Guid.NewGuid() } }));
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private RenewalQuoteCommand Command(params Guid[] voucherIds)
        => new() { UserId = _userId, VoucherIds = voucherIds.ToList() };

    private void EnableRenewal(params (string Code, decimal Rate)[] tiers)
    {
        _context.AppSettings.Add(new AppSetting { Key = AppSettingKeys.VoucherRenewalEnabled, Value = "true", UpdatedAtUtc = DateTime.UtcNow });

        foreach (var (code, rate) in tiers)
        {
            _context.AppSettings.Add(new AppSetting { Key = AppSettingKeys.VoucherRenewalTierEnabled(code), Value = "true", UpdatedAtUtc = DateTime.UtcNow });
            _context.AppSettings.Add(new AppSetting
            {
                Key = AppSettingKeys.VoucherRenewalTierRatePerLiter(code),
                Value = rate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        _context.SaveChanges();
    }

    private FuelVoucher SeedVoucher(decimal liters, DateOnly expiry, VoucherStatus status, Guid? ownerId = null)
    {
        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = liters,
            ExpirationDate = expiry,
            VoucherNumber = $"V-{Guid.NewGuid():N}"[..16],
            QrPayload = $"qr-{Guid.NewGuid():N}",
            Status = status,
            AssignedToUserId = ownerId ?? _userId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _context.FuelVouchers.Add(voucher);
        _context.SaveChanges();
        return voucher;
    }

    private void SeedStock(FuelVoucher matching, DateOnly expiry)
    {
        _context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = matching.Provider,
            FuelTypeId = matching.FuelTypeId,
            Liters = matching.Liters,
            ExpirationDate = expiry,
            VoucherNumber = $"S-{Guid.NewGuid():N}"[..16],
            QrPayload = $"qr-{Guid.NewGuid():N}",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    private void SeedActiveUser()
    {
        var role = new Role { Id = SeedRoles.UserRoleId, Name = SeedRoles.UserName, CreatedAtUtc = DateTime.UtcNow };
        _context.Roles.Add(role);
        _context.Users.Add(new User
        {
            Id = _userId,
            PhoneNumber = "+380110010203",
            RoleId = role.Id,
            Role = role,
            IsActive = true,
            IsDeleted = false,
            TokenVersion = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.SaveChanges();
    }
}
