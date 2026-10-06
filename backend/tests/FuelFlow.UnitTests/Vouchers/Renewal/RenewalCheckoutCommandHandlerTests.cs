using FluentAssertions;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.Renewal.Checkout;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.Vouchers.Renewal;

/// <summary>
/// Unit coverage for the renewal checkout handler (Slice 3): every eligibility/pricing gate, the
/// two-branch resolution at quote time, the no-stock rejection, and the idempotency reuse. Nothing
/// here mutates a voucher — the handler only validates, prices and records the intent as an
/// Order + line items + <see cref="VoucherRenewalItem"/> rows behind one Monobank invoice.
/// </summary>
public sealed class RenewalCheckoutCommandHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RenewalCheckoutCommandHandler _handler;
    private readonly Mock<IMonobankClient> _monobankClientMock;
    private readonly Guid _userId = Guid.NewGuid();

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public RenewalCheckoutCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _context = new ApplicationDbContext(options);
        SeedActiveUser();

        _monobankClientMock = new Mock<IMonobankClient>();
        _monobankClientMock
            .Setup(x => x.CreateInvoiceAsync(It.IsAny<MonobankInvoiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceResponse { InvoiceId = "INV-RENEW", PageUrl = "https://pay.test/INV-RENEW" });

        var monobankOptions = new Mock<IOptions<MonobankOptions>>();
        monobankOptions.Setup(o => o.Value).Returns(new MonobankOptions
        {
            Token = "test_token",
            WebhookUrl = "https://test.local/webhook",
            RedirectUrl = "https://test.local/redirect",
            BaseUrl = "https://api.test.local",
            Enabled = false
        });

        _handler = new RenewalCheckoutCommandHandler(
            _context,
            _monobankClientMock.Object,
            monobankOptions.Object,
            new RuntimeSettingsService(_context),
            NotificationDispatcher.Disabled,
            new Mock<ILogger<RenewalCheckoutCommandHandler>>().Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // ---- Happy paths ---------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ExtendBranch_ShouldCreatePendingOrderWithLineItemsAndRenewalItems()
    {
        EnableRenewal(("1m", 10m));
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(5), status: VoucherStatus.Assigned);

        var response = await _handler.HandleAsync(Command((source.Id, "1m")));

        response.OrderId.Should().NotBe(Guid.Empty);
        response.MonobankInvoiceId.Should().Be("INV-RENEW");
        response.TotalUah.Should().Be(500); // 50 L × 10 UAH/L

        var order = await _context.Orders
            .Include(o => o.LineItems)
            .FirstAsync(o => o.Id == response.OrderId);
        order.Status.Should().Be(OrderStatus.PendingPayment);
        order.Price.Should().Be(500);
        order.LegalEntityId.Should().BeNull();
        order.IdempotencyKey.Should().StartWith("renew:");

        var line = order.LineItems.Should().ContainSingle().Subject;
        line.Provider.Should().Be(source.Provider);
        line.FuelTypeId.Should().Be(source.FuelTypeId);
        line.Liters.Should().Be(50m);
        line.Quantity.Should().Be(1);
        line.LineTotal.Should().Be(500);

        var renewalItem = await _context.VoucherRenewalItems.SingleAsync(i => i.OrderId == response.OrderId);
        renewalItem.SourceVoucherId.Should().Be(source.Id);
        renewalItem.TermCode.Should().Be("1m");
        renewalItem.FulfilledVoucherId.Should().BeNull();
        renewalItem.AmountPaid.Should().Be(500, "the paid amount is frozen for the history timeline");
    }

    [Fact]
    public async Task Handle_ReplaceBranch_WithStockInHand_ShouldCreateOrder()
    {
        EnableRenewal(("1m", 12m));
        // Source has lapsed → Replace branch. It still needs matching stock at offer time.
        var source = SeedVoucher(liters: 40m, expiry: Today.AddDays(-3), status: VoucherStatus.Assigned);
        SeedStock(source, expiry: Today.AddMonths(6));

        var response = await _handler.HandleAsync(Command((source.Id, "1m")));

        response.TotalUah.Should().Be(480); // 40 L × 12 UAH/L
        (await _context.Orders.CountAsync()).Should().Be(1);
        (await _context.VoucherRenewalItems.CountAsync(i => i.OrderId == response.OrderId)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_MixedBatch_ShouldSumPerVoucherLineAmounts()
    {
        EnableRenewal(("1w", 5m), ("3m", 20m));
        var extend = SeedVoucher(liters: 30m, expiry: Today.AddDays(7), status: VoucherStatus.Assigned);
        var replace = SeedVoucher(liters: 50m, expiry: Today.AddDays(-1), status: VoucherStatus.Expired);
        SeedStock(replace, expiry: Today.AddMonths(6));

        var response = await _handler.HandleAsync(Command((extend.Id, "1w"), (replace.Id, "3m")));

        // 30 × 5 = 150, 50 × 20 = 1000 → 1150
        response.TotalUah.Should().Be(1150);
        (await _context.VoucherRenewalItems.CountAsync(i => i.OrderId == response.OrderId)).Should().Be(2);
    }

    // ---- Validation gates ----------------------------------------------------------------------

    [Fact]
    public async Task Handle_MissingUserId_ShouldThrowArgumentException()
    {
        EnableRenewal(("1m", 10m));
        var command = new RenewalCheckoutCommand { UserId = null, Items = { new RenewalCheckoutItem { VoucherId = Guid.NewGuid(), TermCode = "1m" } } };

        await Assert.ThrowsAsync<ArgumentException>(() => _handler.HandleAsync(command));
    }

    [Fact]
    public async Task Handle_EmptyBatch_ShouldThrowEmptyBatch()
    {
        EnableRenewal(("1m", 10m));
        var command = new RenewalCheckoutCommand { UserId = _userId };

        var ex = await Assert.ThrowsAsync<VoucherRenewalException>(() => _handler.HandleAsync(command));
        ex.Code.Should().Be("empty_batch");
    }

    [Fact]
    public async Task Handle_TooManyItems_ShouldThrowTooManyItems()
    {
        EnableRenewal(("1m", 10m));
        var command = new RenewalCheckoutCommand { UserId = _userId };
        for (var i = 0; i < 51; i++)
            command.Items.Add(new RenewalCheckoutItem { VoucherId = Guid.NewGuid(), TermCode = "1m" });

        var ex = await Assert.ThrowsAsync<VoucherRenewalException>(() => _handler.HandleAsync(command));
        ex.Code.Should().Be("too_many_items");
    }

    [Fact]
    public async Task Handle_DuplicateVoucher_ShouldThrowDuplicateVoucher()
    {
        EnableRenewal(("1m", 10m));
        var id = Guid.NewGuid();
        var command = Command((id, "1m"), (id, "1m"));

        var ex = await Assert.ThrowsAsync<VoucherRenewalException>(() => _handler.HandleAsync(command));
        ex.Code.Should().Be("duplicate_voucher");
    }

    [Fact]
    public async Task Handle_FeatureDisabled_ShouldThrowRenewalDisabled()
    {
        // No config seeded → fail-safe off.
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(5), status: VoucherStatus.Assigned);

        var ex = await Assert.ThrowsAsync<VoucherRenewalException>(() => _handler.HandleAsync(Command((source.Id, "1m"))));
        ex.Code.Should().Be("renewal_disabled");
    }

    [Fact]
    public async Task Handle_InactiveUser_ShouldThrowAccountInactive()
    {
        EnableRenewal(("1m", 10m));
        var user = await _context.Users.AsTracking().FirstAsync(u => u.Id == _userId);
        user.IsActive = false;
        await _context.SaveChangesAsync();

        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(5), status: VoucherStatus.Assigned);

        await Assert.ThrowsAsync<AccountInactiveException>(() => _handler.HandleAsync(Command((source.Id, "1m"))));
    }

    [Fact]
    public async Task Handle_VoucherNotOwnedByCaller_ShouldThrowNotYourVoucher()
    {
        EnableRenewal(("1m", 10m));
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(5), status: VoucherStatus.Assigned, ownerId: Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<VoucherRenewalException>(() => _handler.HandleAsync(Command((source.Id, "1m"))));
        ex.Code.Should().Be("not_your_voucher");
    }

    [Fact]
    public async Task Handle_UnknownTermCode_ShouldThrowUnknownTerm()
    {
        EnableRenewal(("1m", 10m));
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(5), status: VoucherStatus.Assigned);

        var ex = await Assert.ThrowsAsync<VoucherRenewalException>(() => _handler.HandleAsync(Command((source.Id, "9y"))));
        ex.Code.Should().Be("unknown_term");
    }

    [Fact]
    public async Task Handle_VoucherStillValidBeyondWindow_ShouldThrowNotRenewable()
    {
        EnableRenewal(("1m", 10m)); // default threshold 14 days
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(60), status: VoucherStatus.Assigned);

        var ex = await Assert.ThrowsAsync<VoucherRenewalException>(() => _handler.HandleAsync(Command((source.Id, "1m"))));
        ex.Code.Should().Be("not_renewable");
    }

    [Fact]
    public async Task Handle_TierEnabledButUnpriced_ShouldThrowTierUnavailable()
    {
        // 1m enabled but rate 0 ⇒ not offerable (fail-safe: never charge 0 for a renewal).
        EnableRenewal(("1m", 0m));
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(5), status: VoucherStatus.Assigned);

        var ex = await Assert.ThrowsAsync<VoucherRenewalException>(() => _handler.HandleAsync(Command((source.Id, "1m"))));
        ex.Code.Should().Be("tier_unavailable");
    }

    [Fact]
    public async Task Handle_ReplaceBranch_WithNoMatchingStock_ShouldThrowNoStock()
    {
        EnableRenewal(("1m", 10m));
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(-2), status: VoucherStatus.Expired);
        // No stock seeded at all.

        var ex = await Assert.ThrowsAsync<VoucherRenewalException>(() => _handler.HandleAsync(Command((source.Id, "1m"))));
        ex.Code.Should().Be("no_stock");

        // Rejected before any order was minted.
        (await _context.Orders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Handle_ReplaceBranch_WithUnderTermStock_ShouldThrowNoStock()
    {
        EnableRenewal(("3m", 10m));
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(-2), status: VoucherStatus.Expired);
        // Stock exists but expires before today + 3 months, so it can't back a 3m replacement.
        SeedStock(source, expiry: Today.AddMonths(1));

        var ex = await Assert.ThrowsAsync<VoucherRenewalException>(() => _handler.HandleAsync(Command((source.Id, "3m"))));
        ex.Code.Should().Be("no_stock");
    }

    [Fact]
    public async Task Handle_TwoReplaceLines_ShouldRequireDistinctStock()
    {
        EnableRenewal(("1m", 10m));
        var a = SeedVoucher(liters: 50m, expiry: Today.AddDays(-1), status: VoucherStatus.Expired);
        var b = SeedVoucher(liters: 50m, expiry: Today.AddDays(-1), status: VoucherStatus.Expired);
        // Only ONE matching stock voucher for two replace lines.
        SeedStock(a, expiry: Today.AddMonths(6));

        var ex = await Assert.ThrowsAsync<VoucherRenewalException>(() => _handler.HandleAsync(Command((a.Id, "1m"), (b.Id, "1m"))));
        ex.Code.Should().Be("no_stock");
    }

    // ---- Idempotency ---------------------------------------------------------------------------

    [Fact]
    public async Task Handle_DuplicateCheckoutWithinBucket_ShouldReuseExistingOrderAndInvoice()
    {
        EnableRenewal(("1m", 10m));
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(5), status: VoucherStatus.Assigned);

        var first = await _handler.HandleAsync(Command((source.Id, "1m")));
        var second = await _handler.HandleAsync(Command((source.Id, "1m")));

        second.OrderId.Should().Be(first.OrderId);
        second.MonobankInvoiceId.Should().Be(first.MonobankInvoiceId);
        (await _context.Orders.CountAsync()).Should().Be(1);

        // The invoice is minted once; the resend collapses onto the live pending order.
        _monobankClientMock.Verify(
            x => x.CreateInvoiceAsync(It.IsAny<MonobankInvoiceRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_RepeatAfterPreviousOrderSettled_ShouldMintNewOrder()
    {
        EnableRenewal(("1m", 10m));
        var source = SeedVoucher(liters: 50m, expiry: Today.AddDays(5), status: VoucherStatus.Assigned);

        var first = await _handler.HandleAsync(Command((source.Id, "1m")));

        var paid = await _context.Orders.AsTracking().FirstAsync(o => o.Id == first.OrderId);
        paid.Status = OrderStatus.PendingFulfillment;
        await _context.SaveChangesAsync();

        var second = await _handler.HandleAsync(Command((source.Id, "1m")));

        second.OrderId.Should().NotBe(first.OrderId);
        (await _context.Orders.CountAsync()).Should().Be(2);
        (await _context.Orders.Select(o => o.IdempotencyKey).Distinct().CountAsync()).Should().Be(2);
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private RenewalCheckoutCommand Command(params (Guid VoucherId, string TermCode)[] items)
    {
        var command = new RenewalCheckoutCommand { UserId = _userId };
        foreach (var (voucherId, termCode) in items)
            command.Items.Add(new RenewalCheckoutItem { VoucherId = voucherId, TermCode = termCode });
        return command;
    }

    private void EnableRenewal(params (string Code, decimal Rate)[] tiers)
    {
        _context.AppSettings.Add(new AppSetting
        {
            Key = AppSettingKeys.VoucherRenewalEnabled,
            Value = "true",
            UpdatedAtUtc = DateTime.UtcNow
        });

        foreach (var (code, rate) in tiers)
        {
            _context.AppSettings.Add(new AppSetting
            {
                Key = AppSettingKeys.VoucherRenewalTierEnabled(code),
                Value = "true",
                UpdatedAtUtc = DateTime.UtcNow
            });
            _context.AppSettings.Add(new AppSetting
            {
                Key = AppSettingKeys.VoucherRenewalTierRatePerLiter(code),
                Value = rate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        _context.SaveChanges();
    }

    /// <summary>
    /// Seeds a customer voucher. The provider term defaults to well past the customer term so the
    /// date rules under test are not shadowed by the ceiling; pass <paramref name="providerExpiry"/>
    /// explicitly to exercise the ceiling itself.
    /// </summary>
    private FuelVoucher SeedVoucher(
        decimal liters, DateOnly expiry, VoucherStatus status, Guid? ownerId = null,
        DateOnly? providerExpiry = null)
    {
        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = liters,
            ProviderExpirationDate = providerExpiry ?? expiry.AddYears(1),
            CustomerExpirationDate = expiry,
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
            ProviderExpirationDate = expiry,
            CustomerExpirationDate = expiry,
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
