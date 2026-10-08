using FluentAssertions;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.Orders;

/// <summary>
/// Buying fuel on a term shorter than the supplier voucher's real life, at a bigger discount.
///
/// The ladder is <c>VoucherTerm:Tier:{code}:DiscountPerLiter</c> — a discount, not an absolute price,
/// so the sell price keeps coming from the cost + margin engine and can never be configured under cost.
/// Everything here defaults to OFF, which must reproduce today's behaviour exactly: full remaining term
/// at the undiscounted price.
/// </summary>
public sealed class VoucherTermSaleTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RuntimeSettingsService _settings;
    private readonly Mock<IMonobankClient> _monobank = new();
    private int _invoiceSeq;
    private readonly IOptions<MonobankOptions> _monobankOptions = Options.Create(new MonobankOptions
    {
        Token = "test_token",
        RedirectUrl = "https://pay.test/redirect",
        WebhookUrl = "https://pay.test/webhook"
    });

    public VoucherTermSaleTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _settings = new RuntimeSettingsService(_context);

        _monobank
            .Setup(x => x.CreateInvoiceAsync(
                It.IsAny<MonobankInvoiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MonobankInvoiceResponse
            {
                InvoiceId = $"INV{++_invoiceSeq}",
                PageUrl = $"https://pay.test/INV{_invoiceSeq}"
            });

        _context.Roles.Add(new Role { Id = Guid.NewGuid(), Name = "User", Level = 0, CreatedAtUtc = DateTime.UtcNow });
        _context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380110010203",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "okko-95",
            Name = "A-95",
            StationId = "okko",
            BasePrice = 60,
            DiscountPrice = 60,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelPackages.Add(new FuelPackage
        {
            Id = "pkg-okko-95-10",
            StationId = "okko",
            FuelTypeId = "okko-95",
            FuelName = "A-95",
            Liters = 10m,
            Price = 600,
            OriginalPrice = 600,
            SupplierPricePerLiter = 40m,
            MarginUahPerLiter = 20m,   // cost-plus 60, no pump ceiling → 60 ₴/L
            FinalPricePerLiter = 60m,
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

    private void EnableTermSale(params (string Code, decimal DiscountPerLiter)[] tiers)
    {
        _context.AppSettings.AddRange(new AppSetting
        {
            Key = AppSettingKeys.VoucherTermSaleEnabled,
            Value = "true",
            UpdatedAtUtc = DateTime.UtcNow
        });
        foreach (var (code, discount) in tiers)
        {
            _context.AppSettings.AddRange(
                new AppSetting { Key = AppSettingKeys.VoucherTermTierEnabled(code), Value = "true", UpdatedAtUtc = DateTime.UtcNow },
                new AppSetting
                {
                    Key = AppSettingKeys.VoucherTermTierDiscountPerLiter(code),
                    Value = discount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    UpdatedAtUtc = DateTime.UtcNow
                });
        }
        _context.SaveChanges();
    }

    private BulkCheckoutCommand Command(string? termCode) => new()
    {
        UserId = _context.Users.First().Id,
        Items =
        {
            new CheckoutItem
            {
                Provider = "okko",
                FuelTypeId = "okko-95",
                StationId = "okko",
                StationName = "OKKO",
                Liters = 10,
                Quantity = 1,
                Price = 600,
                TermCode = termCode
            }
        }
    };

    private BulkCheckoutCommandHandler Handler()
        => new(_context, _monobank.Object, _monobankOptions, _settings,
               new Mock<ILogger<BulkCheckoutCommandHandler>>().Object);

    /// <summary>
    /// Puts one <c>Available</c> voucher in stock with the given paper term. The term ladder is gated on
    /// stock now, so any quote that should offer a term needs this behind it.
    /// </summary>
    private void SeedStock(int daysUntilPaperExpiry, string provider = "OKKO", string fuelTypeId = "okko-95", decimal liters = 10m)
    {
        _context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysUntilPaperExpiry),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysUntilPaperExpiry),
            VoucherNumber = $"STOCK-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    // ── The feature is off by default, and off must change nothing ────────────────────────────

    [Fact]
    public async Task TermSaleDisabled_ChargesTheFullUndiscountedPrice()
    {
        // Even with a ladder configured, the master switch being off must not touch the sale.
        EnableTermSale(("1w", 20m));
        _context.AppSettings.First(s => s.Key == AppSettingKeys.VoucherTermSaleEnabled).Value = "false";
        _context.SaveChanges();

        var response = await Handler().HandleAsync(Command("1w"));

        var order = await _context.Orders.Include(o => o.LineItems).FirstAsync(o => o.Id == response.OrderIds[0]);
        order.Price.Should().Be(600m);          // 10 L × 60 ₴/L, undiscounted
        order.LineItems.Single().TermCode.Should().BeNull("no term was actually sold");
    }

    [Fact]
    public async Task TermSaleDisabled_TermRequested_IsIgnoredRatherThanRejected()
    {
        // Fail-safe direction: a settings problem must never stop a customer buying fuel.
        var response = await Handler().HandleAsync(Command("1w"));

        var order = await _context.Orders.Include(o => o.LineItems).FirstAsync(o => o.Id == response.OrderIds[0]);
        order.Price.Should().Be(600m);
        order.LineItems.Single().TermCode.Should().BeNull();
    }

    // ── The discount ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnabledTier_AppliesTheDiscountAndFreezesTheTermOnTheLine()
    {
        EnableTermSale(("1w", 20m));
        SeedStock(daysUntilPaperExpiry: 8);

        var response = await Handler().HandleAsync(Command("1w"));

        var order = await _context.Orders.Include(o => o.LineItems).FirstAsync(o => o.Id == response.OrderIds[0]);
        order.Price.Should().Be(400m);                       // (60 − 20) ₴/L × 10 L
        order.LineItems.Single().TermCode.Should().Be("1w");
    }

    [Fact]
    public async Task UnconfiguredTier_FallsBackToTheFullTerm()
    {
        // Only 1w is configured; asking for 3m must not invent a price for it.
        EnableTermSale(("1w", 20m));

        var response = await Handler().HandleAsync(Command("3m"));

        var order = await _context.Orders.Include(o => o.LineItems).FirstAsync(o => o.Id == response.OrderIds[0]);
        order.Price.Should().Be(600m);
        order.LineItems.Single().TermCode.Should().BeNull();
    }

    [Fact]
    public async Task UnknownTermCode_FallsBackToTheFullTerm()
    {
        EnableTermSale(("1w", 20m));

        var response = await Handler().HandleAsync(Command("99y"));

        var order = await _context.Orders.Include(o => o.LineItems).FirstAsync(o => o.Id == response.OrderIds[0]);
        order.Price.Should().Be(600m);
        order.LineItems.Single().TermCode.Should().BeNull();
    }

    [Fact]
    public async Task EnabledTierWithZeroDiscount_IsNotOfferable()
    {
        // Zero discount means "not configured", so the term falls away rather than being free.
        EnableTermSale(("1w", 0m));

        var response = await Handler().HandleAsync(Command("1w"));

        var order = await _context.Orders.Include(o => o.LineItems).FirstAsync(o => o.Id == response.OrderIds[0]);
        order.Price.Should().Be(600m);
        order.LineItems.Single().TermCode.Should().BeNull();
    }

    [Fact]
    public async Task DisabledTier_FallsBackToTheFullTerm()
    {
        EnableTermSale(("1w", 20m));
        _context.AppSettings.First(s => s.Key == AppSettingKeys.VoucherTermTierEnabled("1w")).Value = "false";
        _context.SaveChanges();

        var response = await Handler().HandleAsync(Command("1w"));

        var order = await _context.Orders.Include(o => o.LineItems).FirstAsync(o => o.Id == response.OrderIds[0]);
        order.Price.Should().Be(600m);
    }

    [Fact]
    public async Task ShorterTerm_CostsMoreThanTheFullTerm()
    {
        // The whole incentive, stated as a test: the ladder must rise with the term.
        EnableTermSale(("1w", 20m), ("1m", 5m));
        SeedStock(daysUntilPaperExpiry: 40);   // paper term past 1m, so both tiers are backed

        var shortTerm = await Handler().HandleAsync(Command("1w"));
        var longTerm = await Handler().HandleAsync(Command("1m"));

        var shortOrder = await _context.Orders.AsNoTracking().FirstAsync(o => o.Id == shortTerm.OrderIds[0]);
        var longOrder = await _context.Orders.AsNoTracking().FirstAsync(o => o.Id == longTerm.OrderIds[0]);

        longOrder.Price.Should().BeGreaterThan(shortOrder.Price);
        shortOrder.Price.Should().Be(400m);
        longOrder.Price.Should().Be(550m);
    }

    // ── The discount must not become an under-cost sale ───────────────────────────────────────

    [Fact]
    public async Task DiscountPushingBelowCost_IsBlockedByTheExistingGuard()
    {
        // The ladder is a discount on top of the cost+margin price, so the below-cost guard has to see
        // the discounted figure — otherwise a generous tier becomes a silent under-cost sale.
        EnableTermSale(("1w", 25m));   // 60 → 35 ₴/L against a 40 ₴/L supplier cost

        var act = () => Handler().HandleAsync(Command("1w"));

        await act.Should().ThrowAsync<BelowCostSaleBlockedException>();
    }

    [Fact]
    public async Task DiscountDownToExactlyCost_IsAllowed_BecauseItIsZeroMarginNotALoss()
    {
        // The guard is strictly "below cost", matching FuelPricing.IsBelowCost. Landing exactly on the
        // supplier cost means zero margin, not a loss, so it is sellable - same rule as every other line.
        EnableTermSale(("1w", 20m));   // exactly the 40 ₴/L supplier cost
        SeedStock(daysUntilPaperExpiry: 8);

        var response = await Handler().HandleAsync(Command("1w"));

        var order = await _context.Orders.AsNoTracking().FirstAsync(o => o.Id == response.OrderIds[0]);
        order.Price.Should().Be(400m);
    }

    [Fact]
    public async Task DiscountOneTickBelowCost_IsBlocked()
    {
        EnableTermSale(("1w", 20.01m));   // 39.99 ₴/L against a 40 ₴/L cost

        var act = () => Handler().HandleAsync(Command("1w"));

        await act.Should().ThrowAsync<BelowCostSaleBlockedException>();
    }

    [Fact]
    public async Task DiscountLargerThanThePrice_ClampsAtZeroRatherThanPayingTheCustomer()
    {
        // Absurdly generous tier: 60 ₴/L price, 500 ₴/L discount. Normally the below-cost guard would
        // reject this outright; with the fuel opted in, the clamp is what has to hold - a bad discount
        // must never turn into money going the other way.
        _context.FuelTypes.First(f => f.Id == "okko-95").AllowBelowCost = true;
        _context.SaveChanges();
        EnableTermSale(("1w", 500m));
        SeedStock(daysUntilPaperExpiry: 8);

        var response = await Handler().HandleAsync(Command("1w"));

        var order = await _context.Orders.AsNoTracking().FirstAsync(o => o.Id == response.OrderIds[0]);
        order.Price.Should().Be(0m);
    }

    // ── Idempotency must treat the term as part of the cart ──────────────────────────────────

    [Fact]
    public async Task SameCartOnADifferentTerm_DoesNotReuseThePendingInvoice()
    {
        // The cart signature drives invoice reuse while an order is still awaiting payment. If the term
        // were not part of it, switching term would silently reuse the invoice minted at the old price
        // and the customer would be charged the undiscounted amount.
        EnableTermSale(("1w", 20m), ("1m", 5m));
        SeedStock(daysUntilPaperExpiry: 40);   // paper term past 1m, so both tiers sell

        var first = await Handler().HandleAsync(Command("1w"));

        var second = await Handler().HandleAsync(Command("1m"));

        second.OrderIds[0].Should().NotBe(first.OrderIds[0]);
        second.MonobankInvoiceId.Should().NotBe(first.MonobankInvoiceId);
    }

    [Fact]
    public async Task IdenticalCartAndTerm_ReusesThePendingInvoice()
    {
        // The behaviour that guard has to preserve: the mobile client auto-retries the same POST on
        // timeout, and that must collapse onto one order rather than double-charging.
        EnableTermSale(("1w", 20m));
        SeedStock(daysUntilPaperExpiry: 8);

        var first = await Handler().HandleAsync(Command("1w"));
        var retry = await Handler().HandleAsync(Command("1w"));

        retry.OrderIds.Should().ContainSingle().Which.Should().Be(first.OrderIds[0]);
        retry.MonobankInvoiceId.Should().Be(first.MonobankInvoiceId);
    }

    // ── The config itself ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Config_IsOffWithNoTiersOfferableByDefault()
    {
        var config = await _settings.GetVoucherTermConfigAsync();

        config.Enabled.Should().BeFalse();
        config.Tiers.Should().OnlyContain(t => !t.IsOfferable);
    }

    [Fact]
    public async Task Config_MissingTierRow_ResolvesToZeroDiscount()
    {
        EnableTermSale(("1w", 20m));

        var config = await _settings.GetVoucherTermConfigAsync();

        config.Tier(VoucherRenewalTerm.OneWeek)!.IsOfferable.Should().BeTrue();
        config.Tier(VoucherRenewalTerm.OneWeek)!.DiscountPerLiterUah.Should().Be(20m);
        config.Tier(VoucherRenewalTerm.SixMonths)!.IsOfferable.Should().BeFalse();
        config.Tier(VoucherRenewalTerm.SixMonths)!.DiscountPerLiterUah.Should().Be(0m);
    }

    [Fact]
    public async Task Config_MalformedDiscountValue_FallsBackToZeroRatherThanThrowing()
    {
        EnableTermSale(("1w", 20m));
        _context.AppSettings.First(s => s.Key == AppSettingKeys.VoucherTermTierDiscountPerLiter("1w")).Value = "not-a-number";
        _context.SaveChanges();

        var config = await _settings.GetVoucherTermConfigAsync();

        config.Tier(VoucherRenewalTerm.OneWeek)!.DiscountPerLiterUah.Should().Be(0m);
        config.Tier(VoucherRenewalTerm.OneWeek)!.IsOfferable.Should().BeFalse();
    }

    // ── The quote the picker renders ───────────────────────────────────────────────────────────
    //
    // The quote is the only place the customer learns a term's price, so it has to agree with what
    // checkout will do. A mismatch in either direction is a bug: showing more than is sellable sends the
    // customer to a rejected payment, showing less hides a term that would have worked.

    private TermQuoteQueryHandler QuoteHandler() => new(_context, _settings);

    [Fact]
    public async Task Quote_ReportsTheFeatureOffByDefault()
    {
        // The master switch drives whether the picker renders at all, so the quote has to carry it.
        var quote = await QuoteHandler().HandleAsync("okko", "okko-95", 10m);

        quote.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Quote_PricesEveryConfiguredTierFromTheServerCatalog()
    {
        EnableTermSale(("1w", 20m), ("1m", 5m));
        SeedStock(daysUntilPaperExpiry: 40);   // paper term past 1m, so both tiers are backed

        var quote = await QuoteHandler().HandleAsync("okko", "okko-95", 10m);

        quote.Enabled.Should().BeTrue();

        var week = quote.Terms.Single(t => t.Term == "1w");
        week.DiscountPerLiterUah.Should().Be(20m);
        week.PricePerLiterUah.Should().Be(40m);      // 60 − 20
        week.LinePriceUah.Should().Be(400m);        // 40 × 10 L
        week.Available.Should().BeTrue();

        var month = quote.Terms.Single(t => t.Term == "1m");
        month.LinePriceUah.Should().Be(550m);       // 55 × 10 L
        month.Available.Should().BeTrue();
    }

    [Fact]
    public async Task Quote_LeavesAnUnconfiguredTierUnbuyableRatherThanFree()
    {
        // Only 1w is priced. Every other tier still comes back so the ladder renders in full, but it must
        // be unbuyable — a free 6-month term is exactly the kind of misconfiguration that must not sell.
        EnableTermSale(("1w", 20m));

        var quote = await QuoteHandler().HandleAsync("okko", "okko-95", 10m);

        quote.Terms.Should().Contain(t => t.Term == "6m");
        var six = quote.Terms.Single(t => t.Term == "6m");
        six.DiscountPerLiterUah.Should().Be(0m);
        six.Available.Should().BeFalse();
    }

    [Fact]
    public async Task Quote_AgreesWithCheckoutOnAPricedTier()
    {
        // The figure the customer is shown must be the figure charged. Anything else and the picker is
        // lying about the price of the exact line they are about to buy.
        EnableTermSale(("1w", 20m));
        SeedStock(daysUntilPaperExpiry: 8);

        var quoted = (await QuoteHandler().HandleAsync("okko", "okko-95", 10m))
            .Terms.Single(t => t.Term == "1w");
        var checkout = await Handler().HandleAsync(Command("1w"));
        var order = await _context.Orders.AsNoTracking().FirstAsync(o => o.Id == checkout.OrderIds[0]);

        quoted.LinePriceUah.Should().Be(order.Price);
    }

    [Fact]
    public async Task Quote_HidesATierThatWouldSellUnderCost()
    {
        // 60 → 35 ₴/L against a 40 ₴/L supplier cost: checkout refuses this outright, so the picker must
        // not offer it. Showing it would walk the customer to a guaranteed failed payment.
        EnableTermSale(("1w", 25m));
        SeedStock(daysUntilPaperExpiry: 8);   // stock covers the 1w term, so below-cost is the only reason

        var quote = await QuoteHandler().HandleAsync("okko", "okko-95", 10m);

        var week = quote.Terms.Single(t => t.Term == "1w");
        week.Available.Should().BeFalse();
        week.DiscountPerLiterUah.Should().Be(25m, "the tier still exists, it is just not sellable here");
    }

    [Fact]
    public async Task Quote_HidesATierForAFuelAllowedToSellUnderCost()
    {
        // The opt-in fuel reverses the guard, so the tier becomes genuinely sellable and must be offered.
        _context.FuelTypes.First(f => f.Id == "okko-95").AllowBelowCost = true;
        _context.SaveChanges();
        EnableTermSale(("1w", 25m));
        SeedStock(daysUntilPaperExpiry: 8);

        var quote = await QuoteHandler().HandleAsync("okko", "okko-95", 10m);

        quote.Terms.Single(t => t.Term == "1w").Available.Should().BeTrue();
    }

    [Fact]
    public async Task Quote_SurvivesAMissingPackage()
    {
        // The catalog can lose the package between browsing and checkout. The line is refused at checkout
        // anyway, so the quote must degrade to "nothing to buy here" instead of failing the screen.
        EnableTermSale(("1w", 20m));

        var quote = await QuoteHandler().HandleAsync("okko", "okko-95", 999m);

        quote.Enabled.Should().BeTrue();
        quote.Terms.Should().OnlyContain(t => !t.Available);
        quote.Terms.Should().OnlyContain(t => t.PricePerLiterUah == null);
    }

    // ── The ladder is stock-aware: a term the station cannot honour is not offered ──────────────
    //
    // #182: the picker used to offer any term the manager priced, so the customer paid the long-term
    // price and fulfilment clamped the delivered validity down to the stock's paper term. A term has to
    // be backed by stock that outlives it, or it must not be on the ladder at all.

    [Fact]
    public async Task Quote_WithNoStockAtAll_OffersNoTerms()
    {
        EnableTermSale(("1w", 20m), ("1m", 5m), ("6m", 1m));

        var quote = await QuoteHandler().HandleAsync("okko", "okko-95", 10m);

        quote.Enabled.Should().BeTrue();
        quote.Terms.Should().OnlyContain(t => !t.Available, "nothing on the shelf can back any term");
        quote.Terms.Single(t => t.Term == "1w").DiscountPerLiterUah.Should().Be(20m, "the tier still exists, it is just not buyable");
    }

    [Fact]
    public async Task Quote_OffersOnlyTheTermsTheStockCanHonour()
    {
        // One voucher on the shelf with ~3 weeks of paper life: 1w and 2w are covered, 1m and beyond are
        // not. The longer terms stay off the ladder rather than selling validity that will be clamped.
        EnableTermSale(("1w", 20m), ("2w", 15m), ("1m", 5m), ("6m", 1m));
        SeedStock(daysUntilPaperExpiry: 21);

        var quote = await QuoteHandler().HandleAsync("okko", "okko-95", 10m);

        quote.Terms.Single(t => t.Term == "1w").Available.Should().BeTrue();
        quote.Terms.Single(t => t.Term == "2w").Available.Should().BeTrue();
        quote.Terms.Single(t => t.Term == "1m").Available.Should().BeFalse();
        quote.Terms.Single(t => t.Term == "6m").Available.Should().BeFalse();
    }

    [Fact]
    public async Task Quote_IgnoresStockThatIsNotAvailableForSale()
    {
        // An assigned voucher already belongs to someone else — it must not make a term look buyable.
        EnableTermSale(("1w", 20m));
        _context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 10m,
            ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(6),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(6),
            VoucherNumber = "OKKO-ASSIGNED",
            QrPayload = Guid.NewGuid().ToString(),
            Status = VoucherStatus.Assigned,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.SaveChanges();

        var quote = await QuoteHandler().HandleAsync("okko", "okko-95", 10m);

        quote.Terms.Single(t => t.Term == "1w").Available.Should().BeFalse();
    }

    [Fact]
    public async Task Quote_MatchesStockCaseInsensitively()
    {
        // The catalog and order lines carry "okko"; imported stock carries "OKKO". A case-sensitive match
        // would hide every term from the customer despite the shelf being full.
        EnableTermSale(("1w", 20m));
        SeedStock(daysUntilPaperExpiry: 8, provider: "OKKO");

        var quote = await QuoteHandler().HandleAsync("okko", "okko-95", 10m);

        quote.Terms.Single(t => t.Term == "1w").Available.Should().BeTrue();
    }

    [Fact]
    public async Task Checkout_RefusesATermNoStockCanHonour()
    {
        // Defence in depth: even if a stale client posts a term the picker no longer offers, checkout must
        // refuse it rather than charge the long-term price for a validity that gets clamped.
        EnableTermSale(("1m", 5m));

        var act = () => Handler().HandleAsync(Command("1m"));

        await act.Should().ThrowAsync<TermStockUnavailableException>();
        (await _context.Orders.AsNoTracking().CountAsync()).Should().Be(0, "no order may be created");
    }

    [Fact]
    public async Task Checkout_AcceptsATermTheStockCanHonour()
    {
        EnableTermSale(("1w", 20m));
        SeedStock(daysUntilPaperExpiry: 8);

        var response = await Handler().HandleAsync(Command("1w"));

        var order = await _context.Orders.AsNoTracking().FirstAsync(o => o.Id == response.OrderIds[0]);
        order.Price.Should().Be(400m);
    }

    [Fact]
    public async Task Checkout_FullTermSale_NeedsNoStockGate()
    {
        // The feature off (or no term asked for) sells the voucher's full remaining life, which any stock
        // satisfies. That path must not start failing because a stock check was bolted onto it.
        var response = await Handler().HandleAsync(Command(termCode: null));

        var order = await _context.Orders.AsNoTracking().Include(o => o.LineItems)
            .FirstAsync(o => o.Id == response.OrderIds[0]);
        order.LineItems.Single().TermCode.Should().BeNull();
    }
}
