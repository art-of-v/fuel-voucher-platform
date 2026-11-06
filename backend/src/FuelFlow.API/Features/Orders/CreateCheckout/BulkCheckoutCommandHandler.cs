using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.Terms;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Orders.SettlePaidOrder;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;

namespace FuelFlow.Features.Orders.CreateCheckout;

public sealed class BulkCheckoutCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IMonobankClientFactory _monobankClientFactory;
private readonly IMonobankMerchantResolver _merchantResolver;
private readonly RuntimeSettingsService _runtimeSettings;
    private readonly MonobankOptions _monobankOptions;
    private readonly ILogger<BulkCheckoutCommandHandler> _logger;
    private readonly NotificationDispatcher _notifications;
    private readonly PackageStockAvailability _stock;
    private readonly FuelFlowMetrics _metrics;
    private readonly SettleFromHeldMoneyCommandHandler _heldMoney;

    public BulkCheckoutCommandHandler(
        ApplicationDbContext context,
        IMonobankClientFactory monobankClientFactory,
        IMonobankMerchantResolver merchantResolver,
        IOptions<MonobankOptions> monobankOptions,
        RuntimeSettingsService runtimeSettings,
        ILogger<BulkCheckoutCommandHandler> logger,
        NotificationDispatcher notifications,
        PackageStockAvailability stock,
        FuelFlowMetrics metrics,
        SettleFromHeldMoneyCommandHandler heldMoney)
    {
        _context = context;
        _monobankClientFactory = monobankClientFactory;
        _merchantResolver = merchantResolver;
        _monobankOptions = monobankOptions.Value;
        _runtimeSettings = runtimeSettings;
        _notifications = notifications;
        _monobankOptions = monobankOptions.Value;
        _logger = logger;
        _stock = stock;
        _metrics = metrics;
        _heldMoney = heldMoney;
    }

    public async Task<BulkCheckoutResponse> HandleAsync(
        BulkCheckoutCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating bulk checkout for user {UserId} with {ItemCount} items",
            command.UserId, command.Items.Count);

        if (command.UserId == null || command.UserId == Guid.Empty)
            throw new ArgumentException("UserId is required", nameof(command));

        if (command.Items.Count == 0)
            throw new ArgumentException("At least one item is required", nameof(command));

        // Money integrity: a negative or zero quantity would subtract from (or not contribute to)
        // the invoice total while fulfilment still assigns vouchers for the remaining positive
        // lines. Enforced here as well as in the controller so no other caller can skip it.
        foreach (var item in command.Items)
        {
            if (item.Liters <= 0)
                throw new ArgumentException("Liters must be greater than 0 for every item", nameof(command));

            if (item.Quantity <= 0)
                throw new ArgumentException("Quantity must be greater than 0 for every item", nameof(command));
        }

        if (command.LegalEntityId.HasValue)
        {
            var ownsLegalEntity = await _context.LegalEntities
                .AsNoTracking()
                .AnyAsync(x => x.Id == command.LegalEntityId.Value && x.UserId == command.UserId.Value, cancellationToken);

            if (!ownsLegalEntity)
            {
                throw new ArgumentException("Provided LegalEntityId does not belong to the user.", nameof(command));
            }
        }

        var BrandIds = command.Items.Select(i => i.BrandId!.Value).Distinct().ToList();
        var fuelTypeIds = command.Items.Select(i => i.FuelTypeId).ToList();

        // Id to slug for the whole cart, once. Everything below that keys on a brand name - the stock
        // key, the order line, the expiry check - needs it, and the cart names its brands by id.
        var brandSlugs = await _context.Brands.AsNoTracking()
            .Where(b => BrandIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Slug, cancellationToken);

        var fuelTypes = await _context.FuelTypes
            .Where(f => BrandIds.Contains(f.BrandId) && fuelTypeIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

// Inactive accounts keep a read-only session but cannot pay.
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == command.UserId.Value, cancellationToken);

        if (user == null || !user.IsActive || user.IsDeleted)
            throw new AccountInactiveException();

        // The resolved package rides along so the line can record what priced it. The order line stores the
        // resulting price; pricing_basis stores the inputs behind it, which is the only thing that
        // still answers "why was this 89.06?" once the contract and the fuel's pricing have moved on.
        var itemPricing = new List<(CheckoutItem Item, SellablePackage Package, decimal DiscountPerLiter, decimal UnitPrice, decimal LineTotal, decimal OriginalLineTotal, string? TermCode)>();
        var totalPrice = 0m;

        // Term-sale ladder, loaded once. Off by default, in which case every line below keeps the
        // undiscounted full-term behaviour exactly as before.
        var termConfig = await _runtimeSettings.GetVoucherTermConfigAsync(cancellationToken);

        // One stock question for the whole cart, answered before any line is priced. Keyed off the same
        // (Brand, fuel, liters) triple the package lookup uses, so a line can never be priced off a
        // package whose stock is then found missing.
        var wantedStock = command.Items
            .Select(i => StockKey.For(brandSlugs[i.BrandId!.Value], i.FuelTypeId, i.Liters))
            .ToHashSet();
        var inStock = await _stock.AvailableKeysAsync(wantedStock, cancellationToken);

        // Same resolution the single-line path uses, so a cart can never price a line off a different
        // row than that line would have priced off on its own. Previously this read the whole
        // catalogue and picked with a local FirstOrDefault, which is arbitrary when the key has two
        // rows - and it can, because a rehearsal nominal may mirror a real one. See
        // PackageStockAvailability.FindPackagesAsync.
        var packages = await _stock.FindPackagesAsync(wantedStock, cancellationToken);

        // The same question for the whole cart, answered before the loop: how far does the paper
        // term of the stock reach. It used to be asked per line, inside the loop, so a cart of
        // twenty lines asked the database the same thing twenty times.
        var bestStockExpirations = await TermStockCoverage.BestStockExpirationsAsync(
            _context, wantedStock, cancellationToken);
        foreach (var item in command.Items)
        {
            var fuelType = fuelTypes.FirstOrDefault(f =>
                f.Id == item.FuelTypeId && f.BrandId == item.BrandId);

            if (fuelType == null)
                throw new ArgumentException(
                    $"Invalid fuel type ID: {item.FuelTypeId} for Brand {item.BrandId}");

            var package = packages.GetValueOrDefault(StockKey.For(brandSlugs[item.BrandId!.Value], item.FuelTypeId, item.Liters));

            if (package == null)
                throw new ArgumentException(
                    $"No pricing found for fuel type {item.FuelTypeId} at Brand {item.BrandId} for {item.Liters}L");

            // Checked before the pricing guards below, and deliberately so: an empty shelf is the more
            // fundamental answer, and refusing there means the below-cost alert is not fired for a sale
            // that could never have completed anyway. One blocked line fails the whole bulk order, matching
            // the below-cost behaviour — a customer who cannot buy one line cannot check out with it.
            if (!inStock.Contains(StockKey.For(brandSlugs[item.BrandId!.Value], item.FuelTypeId, item.Liters)))
                throw new PackageOutOfStockException(brandSlugs[item.BrandId!.Value], item.FuelTypeId, item.Liters);

            var (discountPerLiter, termCode) = CheckoutTermResolver.Resolve(item, termConfig, command.UserId.Value, _logger);

// Slice-3 hard block (safety net at checkout): refuse a below-cost line unless this
            // supplier+fuel is opted in. One blocked item fails the whole bulk order.
            // Evaluated against the DISCOUNTED figure: FuelPricing.IsBelowCost recomputes the price
            // from cost + margin and would never see a term discount, letting a generous tier sell
            // under cost while the guard reported the line was fine.
            // TWO REFUSALS, and the second is the one that used to be missing. A fuel with NO
            // recorded cost has nothing to compare the price against, so the answer is "cannot tell"
            // - and a "cannot tell" that lets the sale through is not a guard. The catalogue import
            // creates fuels with no cost on purpose (it knows the column price and nothing about
            // what we pay), so this is every fuel the import has ever made, not a corner case.
            var verdict = ServerPricing.TryGetBelowCostVerdict(package, discountPerLiter);
            if (verdict == ServerPricing.BelowCostVerdict.UnknownCost)
            {
                throw new UnknownCostSaleBlockedException(item.FuelTypeId);
            }

            if (!fuelType.AllowBelowCost && verdict == ServerPricing.BelowCostVerdict.BelowCost)
            {
                // Same as the single-line path: a blocked basket tells nobody unless we say so.
                // Throttled per fuel inside the dispatcher, so one customer's retry loop is one message.
                await _notifications.BelowCostAsync(
                    brandSlugs[item.BrandId!.Value], fuelType.Name,
                    package.SupplierPricePerLiter ?? 0m, discountPerLiter, deliberate: false, cancellationToken);
                throw new BelowCostSaleBlockedException(item.FuelTypeId);
            }

            // Defence in depth behind the picker's stock gate, and only after the cheaper below-cost
            // check above. A term the ladder offered can still be unbackable by the time the cart is
            // posted (stock sold, an admin raised the paper terms), and completing the sale would charge
            // the long-term price for a validity fulfilment then clamps down to the stock's paper term.
            // Refuse the stale term instead of silently downgrading it. Only a term that actually
            // resolved is gated: a null termCode is the full-term sale (feature off, or no term asked
            // for), which any stock satisfies.
            if (termCode is not null
                && VoucherRenewalTerms.TryFromCode(termCode, out var purchasedTerm))
            {
                var bestStockExpiration = bestStockExpirations.GetValueOrDefault(
                    StockKey.For(brandSlugs[item.BrandId!.Value], item.FuelTypeId, item.Liters));

                if (!TermStockCoverage.CanHonour(bestStockExpiration, DateOnly.FromDateTime(SystemClock.UtcNow), purchasedTerm))
                {
                    throw new TermStockUnavailableException(item.FuelTypeId);
                }
            }

            var unitPrice = ServerPricing.PackagePrice(package, item.Liters, discountPerLiter);

            // checked: silent int wraparound here would decouple the amount we invoice from
            // the vouchers we hand out. Overflow must fail the request, not wrap to a small total.
            decimal lineTotal;
            try
            {
                lineTotal = checked(unitPrice * item.Quantity);
                totalPrice = checked(totalPrice + lineTotal);
            }
            catch (OverflowException)
            {
                throw new ArgumentException("Order total is too large", nameof(command));
            }

            if (item.Price != lineTotal)
            {
                _logger.LogWarning(
                    "Client price {ClientPrice} does not match server price {ServerPrice} for user {UserId}, fuel {FuelTypeId}; using server price",
                    item.Price, lineTotal, command.UserId, item.FuelTypeId);
            }

            itemPricing.Add((item, package, discountPerLiter, unitPrice, lineTotal,
                ServerPricing.OriginalLineTotal(package, item.Liters, unitPrice, item.Quantity), termCode));
        }

        // The basket: the whole cart, normalized so that the same cart always keys alike, and
        // digested so that the key fits the column no matter how many lines the cart has.
        //
        // This is the path the mobile app actually uses, and the client's fetchWithRetry
        // auto-resends the identical /bulk POST on a timeout, a 5xx or a network error with no user
        // action at all - so two requests for one cart are a fact of life, not a corner case.
        //
        // The key names the cart and nothing else: no clock, no random suffix. A five-minute bucket
        // used to roll over mid-cart, and a retry on the far side of the boundary keyed differently
        // and charged the customer twice.
        var cartSignature = CheckoutBasket.CartSignature(command.Items.Select(i =>
            string.Join(
                ":",
                i.BrandId!.Value.ToString("N"),
                i.FuelTypeId.ToString("N"),
                i.Liters.ToString(CultureInfo.InvariantCulture),
                i.Quantity.ToString(CultureInfo.InvariantCulture),
                i.TermCode ?? "full")));

        var checkoutKey = CheckoutBasket.Key(CheckoutBasket.Cart, command.UserId!.Value, cartSignature);

        var now = SystemClock.UtcNow;

        // Which of the two buttons this request came from - one answer for the whole cart, because it
        // is one tap (planning #889).
        var route = CheckoutRoutes.Resolve(command.SettlementRoute);

        // Asked before anything is written, including before the basket is settled: the customer who is
        // not on the offer must not have their pending order dissolved by a tap that was never going to
        // be allowed.
        CheckoutRoutes.RequirePromoAccess(route, user);

        // The order that already holds this cart, if there is one. A retry or a double tap finds it
        // here and is given the same invoice. The read is an equality on an indexed column; it used
        // to be a prefix match on a key that carried a random suffix, which no b-tree can serve and
        // which two requests could both miss.
        var existingOrder = await CheckoutGuard.FindPendingAsync(_context, checkoutKey, cancellationToken);

        if (existingOrder != null)
        {
            var (state, onBasket) = await CheckoutRoutes.SettleBasketAsync(
                _context, existingOrder, route, now, cancellationToken);

            if (state == CheckoutRoutes.BasketState.Reuse)
            {
                // The attempt that holds this cart is the current one, on the route this request is
                // asking for. Either it has its invoice or it is still getting one; either way it is
                // the same order and the acquirer is not asked twice.
                _logger.LogWarning(
                    "Duplicate bulk checkout; reusing pending order {OrderId} / invoice {InvoiceId}",
                    onBasket!.Id, onBasket.MonobankInvoiceId);

                return new BulkCheckoutResponse
                {
                    OrderIds = [onBasket.Id],
                    SettlementRoute = CheckoutRoutes.Of(onBasket).ToString(),
                    MonobankInvoiceId = onBasket.MonobankInvoiceId,
                    PaymentUrl = onBasket.MonobankPaymentUrl
                };
            }

            if (state == CheckoutRoutes.BasketState.InFlight)
            {
                _logger.LogWarning(
                    "Bulk checkout on the {Route} route refused: order {OrderId} holds the cart on the "
                     + "{HeldRoute} route and may already have money moving on it",
                    route, onBasket!.Id, CheckoutRoutes.Of(onBasket));

                throw new CheckoutAlreadyInPaymentException(onBasket.Id);
            }

            // Free: the cart's previous attempt was walked away from, or was on the other route and
            // has just been given up. This request writes the new one.
        }

        // An order for this cart that held money already paid is the same purchase, not a new one. It
        // left the basket's index the moment it settled, so this read is all that stands between a
        // second tap and a second invoice for fuel whose vouchers are already on their way.
        var paidAlready = await CheckoutGuard.FindPaidFromHeldMoneyAsync(
            _context, checkoutKey, now, cancellationToken);

        if (paidAlready != null)
        {
            _logger.LogWarning(
                "Duplicate bulk checkout for cart {CheckoutKey}; reusing the order held money paid ({OrderId}), "
                 + "which has no invoice to pay again",
                checkoutKey, paidAlready.Id);

            return new BulkCheckoutResponse
            {
                OrderIds = [paidAlready.Id],
                SettlementRoute = CheckoutRoutes.Of(paidAlready).ToString(),
                AlreadyPaid = true
            };
        }

        // Resolve merchant for this account. Outside the try below on purpose: a QA account routed
        // to an unconfigured sandbox is refused outright rather than degraded into an order with
        // no invoice, and never quietly pointed at the live merchant.
        //
        // The declared route resolves none: there is no terminal to choose when no terminal is
        // asked, and the order says so the same way it says it has no invoice.
        var merchant = route == SettlementRoute.Declared
            ? (MonobankMerchant?)null
            : _merchantResolver.Resolve(user.PhoneNumber, user.IsQaAccount);

        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            UserId = command.UserId!.Value,
            LegalEntityId = command.LegalEntityId,
            Price = totalPrice,
            Status = OrderStatus.PendingPayment,
            // Written here and nowhere else; the database refuses a change (see the column).
            SettlementRoute = route,
            // The order's own key: unique per order, and the external reference the acquirer is
            // given when this order is refunded. The random suffix is what keeps it that way - the
            // cart's identity is checkout_key, and two orders may share a cart but may not share
            // this.
            IdempotencyKey = $"{checkoutKey}:{Guid.NewGuid():N}",
            CheckoutKey = checkoutKey,
            MonobankMerchant = merchant,
            CreatedAtUtc = SystemClock.UtcNow,
            UpdatedAtUtc = SystemClock.UtcNow
        };

        foreach (var (item, package, discountPerLiter, unitPrice, lineTotal, originalLineTotal, termCode) in itemPricing)
        {
            order.LineItems.Add(new OrderLineItem
            {
                Id = Guid.CreateVersion7(),
                OrderId = order.Id,
                Brand = brandSlugs[item.BrandId!.Value],
                // The brand's key beside its name. The composite foreign key over (fuel, brand)
                // is what checks the two against each other; until the readers move, both are
                // written.
                BrandId = package.BrandId,
                FuelTypeId = item.FuelTypeId,
                Liters = item.Liters,
                Quantity = item.Quantity,
                UnitPrice = unitPrice,
                LineTotal = lineTotal,
                OriginalLineTotal = originalLineTotal,
                TermCode = termCode,
                PricingBasis = OrderLinePricing.For(package, termCode, discountPerLiter)
            });
        }

        _context.Orders.Add(order);

        // The order is written BEFORE the acquirer is asked for an invoice, and that order is what
        // decides the race. The second request for this cart is refused by the unique index at this
        // line and never reaches the acquirer - so the client's automatic resend cannot become a
        // second invoice, and a second charge.
        //
        // It is also the order the money belongs to: from here on the invoice is attached to a row
        // that exists, whatever happens to the call below.
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CheckoutGuard.IsUniqueViolation(ex))
        {
            // Another request for this cart got there first. Its order is the answer to this one -
            // and this request has not asked the acquirer for anything, so there is no second
            // invoice to cancel.
            _context.ChangeTracker.Clear();

            var winner = await CheckoutGuard.FindPendingAsync(_context, checkoutKey, cancellationToken);
            if (winner == null) throw;

            _logger.LogWarning(
                "Concurrent bulk checkout for basket {CheckoutKey} lost the race; reusing order {OrderId}",
                checkoutKey, winner.Id);

            // A declared winner never gets an invoice, so there is nothing to wait for; the order is
            // the answer on its own.
            var withInvoice = CheckoutRoutes.Of(winner) == SettlementRoute.Declared
                ? winner
                : await CheckoutGuard.WaitForInvoiceAsync(_context, winner.Id, cancellationToken) ?? winner;

            return new BulkCheckoutResponse
            {
                OrderIds = [withInvoice.Id],
                SettlementRoute = CheckoutRoutes.Of(withInvoice).ToString(),
                MonobankInvoiceId = withInvoice.MonobankInvoiceId,
                PaymentUrl = withInvoice.MonobankPaymentUrl
            };
        }

        // Planning #867 trigger 2: the money can arrive before the order does. When one payment of
        // exactly this price is already held for this customer, it pays for the order now, and the
        // acquirer is not asked for an invoice the customer would then pay a second time for the same
        // fuel. Nothing is settled here that the settle core's own rules would not settle for an
        // operator's click; what changes is that nobody has to be watching a tab for it.
        var held = await _heldMoney.HandleAsync(
            new SettleFromHeldMoneyCommand { OrderId = order.Id, Because = "the bulk checkout" },
            cancellationToken);

        if (held.Settled)
        {
            _logger.LogInformation(
                "Bulk checkout for cart {CheckoutKey} settled on {PaymentId} held money; no invoice requested",
                checkoutKey, held.PaymentId);

            return new BulkCheckoutResponse
            {
                OrderIds = [order.Id],
                SettlementRoute = route.ToString(),
                AlreadyPaid = true
            };
        }

        // The declared route ends here: the order exists, the acquirer was never asked, and what it
        // waits for is a declaration. See the same answer on the single-item checkout, and the reason
        // the response carries the route rather than leaving an absent payment link to be read as a
        // failure.
        if (route == SettlementRoute.Declared)
        {
            _logger.LogInformation(
                "Bulk checkout for cart {CheckoutKey} written on the declared route; the acquirer was not asked "
                 + "and order {OrderId} waits on the settlement channel",
                checkoutKey, order.Id);

            return new BulkCheckoutResponse
            {
                OrderIds = [order.Id],
                SettlementRoute = route.ToString()
            };
        }

        // Past the declared route's answer, so this request is on the acquirer one - and that is the
        // route the resolution above was asked about.
        var acquirerMerchant = merchant!.Value;

        MonobankInvoiceResponse invoiceResponse;
        var invoiceStopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var monobankClient = _monobankClientFactory.ForMerchant(acquirerMerchant);

            invoiceResponse = await monobankClient.CreateInvoiceAsync(
                new MonobankInvoiceRequest
                {
                    // Monobank is the one place amounts must be kopecks.
                    Amount = Money.ForMonobank(totalPrice),
                    MerchantPaymentInfo = $"FuelFlow Bundle",
                    RedirectUrl = _monobankOptions.RedirectUrl,
                    WebhookUrl = _monobankOptions.WebhookUrl
                }, cancellationToken);

            _metrics.MonobankInvoiceCreated(invoiceStopwatch.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "Monobank invoice created on {Merchant} merchant for bundle: {InvoiceId}",
                acquirerMerchant,
                invoiceResponse.InvoiceId);
        }
        catch (Exception ex)
        {
            // The same counter the single-item checkout records. Without it the busiest of the two
            // paths was invisible to the acquirer-failure alert, and a merchant whose terminal the
            // bank has switched off produced 14 refusals and no page (planning #873).
            _metrics.MonobankInvoiceFailed(ex.GetType().Name);
            _logger.LogError(ex, "Failed to create Monobank invoice for bundle");

            // The invoice never came, so this attempt is over - and an attempt that is over must
            // not hold the cart against the customer's next try. The order itself stays: it is the
            // record of what was attempted.
            CheckoutGuard.Release(order);
            await _context.SaveChangesAsync(cancellationToken);

            throw;
        }

        order.MonobankInvoiceId = invoiceResponse.InvoiceId;
        order.MonobankPaymentUrl = invoiceResponse.PageUrl;

        // See CreateCheckoutCommandHandler for why the payment is its own row. Same reason here: this
        // is the only moment the invoice exists, so it is the only moment it can be recorded.
        _context.Payments.Add(PaymentFactory.CreateCapture(
            order, acquirerMerchant, invoiceResponse.InvoiceId, SystemClock.UtcNow));

        // The invoice, written onto the order that is already there. A second attempt for this cart
        // is refused by the index above and never reaches this point, so this is the one and only
        // invoice for this cart.
        await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
            "Bulk checkout created with {LineItemCount} line items, order {OrderId}, invoice {InvoiceId}",
            order.LineItems.Count, order.Id, invoiceResponse.InvoiceId);

        return new BulkCheckoutResponse
        {
            OrderIds = [order.Id],
            SettlementRoute = route.ToString(),
            MonobankInvoiceId = invoiceResponse.InvoiceId,
            PaymentUrl = invoiceResponse.PageUrl,
            // Not persisted on the order - AppUrl is an opaque mbnk.app redirector that cannot be
            // reconstructed later, so it is returned once here. See CreateCheckoutCommandHandler.
            AppUrl = invoiceResponse.AppUrl
        };
    }
}
