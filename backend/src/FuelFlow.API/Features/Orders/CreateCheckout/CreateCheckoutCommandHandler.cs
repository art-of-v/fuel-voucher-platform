using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Orders.SettlePaidOrder;
using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Orders.CreateCheckout;

public sealed class CreateCheckoutCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IMonobankClientFactory _monobankClientFactory;
    private readonly IMonobankMerchantResolver _merchantResolver;
    private readonly MonobankOptions _monobankOptions;
    private readonly ILogger<CreateCheckoutCommandHandler> _logger;
    private readonly FuelFlowMetrics _metrics;
    private readonly NotificationDispatcher _notifications;
    private readonly PackageStockAvailability _stock;
    private readonly SettleFromHeldMoneyCommandHandler _heldMoney;

    public CreateCheckoutCommandHandler(
        ApplicationDbContext context,
        IMonobankClientFactory monobankClientFactory,
        IMonobankMerchantResolver merchantResolver,
        IOptions<MonobankOptions> monobankOptions,
        ILogger<CreateCheckoutCommandHandler> logger,
        FuelFlowMetrics metrics,
        NotificationDispatcher notifications,
        PackageStockAvailability stock,
        SettleFromHeldMoneyCommandHandler heldMoney)
    {
        _context = context;
        _monobankClientFactory = monobankClientFactory;
        _merchantResolver = merchantResolver;
        _monobankOptions = monobankOptions.Value;
        _logger = logger;
        _metrics = metrics;
        _notifications = notifications;
        _stock = stock;
        _heldMoney = heldMoney;
    }

    public async Task<CreateCheckoutResponse> HandleAsync(
        CreateCheckoutCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating checkout for user {UserId}", command.UserId);

        if (command.UserId == null || command.UserId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required", nameof(command));
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == command.UserId.Value, cancellationToken);

        if (user == null || !user.IsActive || user.IsDeleted)
            throw new AccountInactiveException();

        if (command.BrandId is null || command.BrandId == Guid.Empty)
        {
            throw new ArgumentException("BrandId is required", nameof(command));
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

        var fuelTypeEntity = await _context.FuelTypes
            .FirstOrDefaultAsync(f => f.Id == command.FuelTypeId && f.BrandId == command.BrandId, cancellationToken);

        if (fuelTypeEntity == null)
        {
            throw new ArgumentException($"Invalid fuel type ID: {command.FuelTypeId} for Brand {command.BrandId}");
        }

        // Resolved through the shared helper, not by a local FirstOrDefaultAsync: a triple is not
        // unique, because a rehearsal nominal can mirror a real one on the same Brand/fuel/size, so
        // an unordered pick returns whichever row Postgres produced first. The helper keeps every
        // caller on the real pool. See PackageStockAvailability.
        var package = await _stock.FindPackageAsync(
            command.BrandId.Value, command.FuelTypeId, command.Liters, cancellationToken);

        if (package == null)
        {
            throw new ArgumentException(
                $"No pricing found for fuel type {command.FuelTypeId} at Brand {command.BrandId} for {command.Liters}L");
        }

        // Same gate as the bulk path, and for the same reason: never mint an invoice for fuel there is no
        // stock to deliver. Checked before the below-cost block so an empty shelf does not fire an
        // operator alert about a sale that was never going to happen. Answers about the same real pool
        // the package above was taken from.
        if (!await _stock.IsInStockAsync(package.BrandSlug, command.FuelTypeId, command.Liters, cancellationToken))
        {
            throw new PackageOutOfStockException(package.BrandSlug, command.FuelTypeId, command.Liters);
        }

        // Slice-3 hard block (safety net at checkout, mirroring the operator write path): refuse a
        // sale whose recomputed price sits below blended cost unless this supplier+fuel is opted in.
        //
        // TWO REFUSALS, and the second one is the one this used to be missing.
        //
        // The block was written as `package.SupplierPricePerLiter is { } cost && ...`, which means a
        // fuel with NO recorded cost skipped the whole check and THE SALE WENT THROUGH. The model
        // already had the honest answer - ServerPricing.TryGetBelowCostVerdict returns UnknownCost
        // for exactly this case - and nobody asked it. A fuel with no cost was freely sellable, at
        // whatever price the package carried.
        //
        // The catalogue import creates fuels with no cost on purpose: it knows the column price
        // (what the driver pays) and nothing about what we pay. So this is not a corner case, it
        // is every fuel the import has ever made. A fuel that is in stock and has no recorded
        // cost cannot be sold, because nobody can prove the price is above what we paid.
        //
        // The discount argument is 0 because THIS path has no term ladder - it sells the full
        // remaining supplier term, so there is no term discount to apply (see the pricing basis
        // further down). The bulk path has terms, and it passes the real one.
        //
        // And it is ServerPricing's verdict rather than FuelPricing's, because ServerPricing
        // compares against the price the customer is ACTUALLY charged. FuelPricing recomputes it
        // from cost + margin, which would never see a term discount - so a generous tier could
        // sell under cost while the guard reported the line was fine.
        var verdict = ServerPricing.TryGetBelowCostVerdict(package, 0m);
        if (verdict == ServerPricing.BelowCostVerdict.UnknownCost)
        {
            throw new UnknownCostSaleBlockedException(command.FuelTypeId);
        }

        if (!fuelTypeEntity.AllowBelowCost && verdict == ServerPricing.BelowCostVerdict.BelowCost)
        {
            // A customer is standing at the payment screen being refused, and nothing tells anyone.
            // The pricing alert only fired on a manager saving a price or costing a batch, so a fuel
            // that drifted below cost blocked sales silently. Throttled per fuel inside the dispatcher,
            // so a customer retrying the same checkout does not flood the chat.
            await _notifications.BelowCostAsync(
                package.BrandSlug, fuelTypeEntity.Name, package.SupplierPricePerLiter,
                ServerPricing.DiscountedPricePerLiter(package, 0m) ?? 0m, deliberate: false, cancellationToken);
            throw new BelowCostSaleBlockedException(command.FuelTypeId);
        }

        var unitPrice = ServerPricing.PackagePrice(package, command.Liters);
        var lineTotal = unitPrice * command.Quantity;

        if (command.Price != lineTotal)
        {
            _logger.LogWarning(
                "Client price {ClientPrice} does not match server price {ServerPrice} for user {UserId}; using server price",
                command.Price, lineTotal, command.UserId);
        }

        // The basket this checkout is for. It names what was bought and by whom and nothing else:
        // no clock, no random suffix. The minute of the clock used to be in the key, rounded down
        // to a five-minute bucket, which made the window discontinuous - a retry one millisecond
        // before the bucket rolled over produced a different key, the dedupe missed, and the
        // customer was charged twice for one tap.
        //
        // This key is what the order is guarded by: it is written on the order, and a unique index
        // over the orders awaiting payment refuses a second one for the same basket.
        var checkoutKey = CheckoutBasket.Key(
            CheckoutBasket.Purchase,
            command.UserId.Value,
            CheckoutBasket.PurchaseSignature(
                command.BrandId.Value, command.FuelTypeId, command.Liters, command.Quantity));

        var now = SystemClock.UtcNow;

        // Which of the two buttons this request came from. Absent is the payment one, which is what
        // every client that has ever called this endpoint meant (planning #889).
        var route = CheckoutRoutes.Resolve(command.SettlementRoute);

        // Asked before anything is written, including before the basket is settled: the customer who is
        // not on the offer must not have their pending order dissolved by a tap that was never going to
        // be allowed.
        CheckoutRoutes.RequirePromoAccess(route, user);

        // The order that already holds this basket, if there is one. A retry or a double tap finds
        // it here and is given the same invoice. The read is an equality on an indexed column; it
        // used to be a prefix match on a key that carried a random suffix, which no b-tree can
        // serve and which two requests could both miss.
        var existingOrder = await CheckoutGuard.FindPendingAsync(_context, checkoutKey, cancellationToken);

        if (existingOrder != null)
        {
            var (state, onBasket) = await CheckoutRoutes.SettleBasketAsync(
                _context, existingOrder, route, now, cancellationToken);

            if (state == CheckoutRoutes.BasketState.Reuse)
            {
                // The attempt that holds this basket is the current one, on the route this request is
                // asking for. Either it has its invoice or it is still getting one; either way it is
                // the same order and the acquirer is not asked twice.
                _logger.LogWarning(
                    "Duplicate order creation attempt detected for basket {CheckoutKey}; reusing order {OrderId}",
                    checkoutKey, onBasket!.Id);

                // On the declared route the reuse is also the second button's own money question:
                // part-funded is refused with the figure, fully funded is paid now (planning #889).
                if (route == SettlementRoute.Declared
                    && await DeclaredMoney.SpendOnReusedOrderAsync(
                        _context, _heldMoney, command.UserId!.Value, onBasket, cancellationToken))
                {
                    return new CreateCheckoutResponse
                    {
                        OrderId = onBasket.Id,
                        Status = OrderStatus.PendingFulfillment.ToString(),
                        SettlementRoute = route.ToString(),
                        AlreadyPaid = true
                    };
                }

                return new CreateCheckoutResponse
                {
                    OrderId = onBasket.Id,
                    Status = onBasket.Status.ToString(),
                    SettlementRoute = CheckoutRoutes.Of(onBasket).ToString(),
                    MonobankInvoiceId = onBasket.MonobankInvoiceId,
                    PaymentUrl = onBasket.MonobankPaymentUrl
                };
            }

            if (state == CheckoutRoutes.BasketState.InFlight)
            {
                _logger.LogWarning(
                    "Checkout for basket {CheckoutKey} on the {Route} route refused: order {OrderId} holds the "
                     + "basket on the {HeldRoute} route and may already have money moving on it",
                    checkoutKey, route, onBasket!.Id, CheckoutRoutes.Of(onBasket));

                throw new CheckoutAlreadyInPaymentException(onBasket.Id);
            }

            // Free. The order that held the basket was either walked away from, or was an attempt on
            // the other route that this request has just given up; in both cases it is no longer in
            // the way, and this request writes a new one.
        }

        // An order for this basket that held money already paid is the same purchase, not a new one.
        // It left the basket's index the moment it settled, so this read is all that stands between a
        // second tap and a second invoice for fuel whose vouchers are already on their way.
        var paidAlready = await CheckoutGuard.FindPaidFromHeldMoneyAsync(
            _context, checkoutKey, SystemClock.UtcNow, cancellationToken);

        if (paidAlready != null)
        {
            _logger.LogWarning(
                "Duplicate checkout for basket {CheckoutKey}; reusing the order held money paid ({OrderId}), "
                 + "which has no invoice to pay again",
                checkoutKey, paidAlready.Id);

            return new CreateCheckoutResponse
            {
                OrderId = paidAlready.Id,
                Status = paidAlready.Status.ToString(),
                SettlementRoute = CheckoutRoutes.Of(paidAlready).ToString(),
                AlreadyPaid = true
            };
        }

        // Item 5 of planning #889, asked before a row exists: money held for this account that does not
        // cover this price is a refusal, not an order that waits half-paid and claims stock. An account
        // holding nothing is the other branch of the ruling and is not refused.
        if (route == SettlementRoute.Declared)
        {
            DeclaredMoney.RefuseIfPartlyFunded(await DeclaredMoney.InspectAsync(
                _context, command.UserId!.Value, lineTotal, null, cancellationToken));
        }

        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            UserId = command.UserId!.Value,
            LegalEntityId = command.LegalEntityId,
            Price = lineTotal,
            Status = OrderStatus.PendingPayment,
            // Decided here and never again: the database raises on a change (see the column's
            // comment), because the route says which of two pockets pays and a later writer cannot
            // answer for what the customer chose.
            SettlementRoute = route,
            // The order's own key: unique per order, and the external reference the acquirer is
            // given when this order is refunded. The random suffix is what keeps it that way - the
            // basket's identity is checkout_key, and two orders may share a basket but may not
            // share this.
            IdempotencyKey = $"{checkoutKey}:{Guid.NewGuid():N}",
            CheckoutKey = checkoutKey,
            CreatedAtUtc = SystemClock.UtcNow,
            UpdatedAtUtc = SystemClock.UtcNow
        };

        order.LineItems.Add(new OrderLineItem
        {
            Id = Guid.CreateVersion7(),
            OrderId = order.Id,
            Brand = package.BrandSlug,
            // The brand's key beside its name. The composite foreign key over (fuel, brand) is
            // what checks the two against each other; until the readers move, both are written.
            BrandId = package.BrandId,
            FuelTypeId = command.FuelTypeId,
            Liters = command.Liters,
            Quantity = command.Quantity,
            UnitPrice = unitPrice,
            LineTotal = lineTotal,
            OriginalLineTotal = ServerPricing.OriginalLineTotal(package, command.Liters, unitPrice, command.Quantity),
            // Same freeze as the bulk path, so a single-line order can explain its price too. This
            // path has no term ladder - it sells the full remaining supplier term - so termCode and
            // the term discount are null and the per-litre columns are the whole basis.
            PricingBasis = OrderLinePricing.For(package, termCode: null, discountPerLiter: null)
        });

        _context.Orders.Add(order);

        // Resolve which Monobank merchant this account must use. Deliberately outside the try
        // below, and it is the one failure here that is not degraded into "order created, no
        // invoice": a QA account pointed at an unconfigured sandbox is refused outright. Sending
        // that account to the live merchant instead would take real money from someone testing,
        // and an order with no payment URL at least cannot be paid by mistake.
        //
        // The declared route resolves none, because on that route it is not a question: there is no
        // terminal to choose when no terminal is asked. The order keeps no merchant for the same
        // reason it keeps no invoice - ck_order_declared_route_asks_no_acquirer is what says so.
        var merchant = route == SettlementRoute.Declared
            ? (MonobankMerchant?)null
            : _merchantResolver.Resolve(user.PhoneNumber, user.IsQaAccount);
        order.MonobankMerchant = merchant;

        // The order is written BEFORE the acquirer is asked for an invoice, and that order is what
        // decides the race. The second request for this basket is refused by the unique index at
        // this line and never reaches the acquirer - so a double tap, and the client's automatic
        // resend of the same POST, cannot become two invoices for one basket.
        //
        // It is also the order the money belongs to: from here on the invoice is attached to a row
        // that exists, whatever happens to the call below.
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CheckoutGuard.IsUniqueViolation(ex))
        {
            // Another request for this basket got there first. Its order is the answer to this
            // one - and this request has not asked the acquirer for anything, so there is no second
            // invoice to cancel.
            _context.ChangeTracker.Clear();

            var winner = await CheckoutGuard.FindPendingAsync(_context, checkoutKey, cancellationToken);
            if (winner == null) throw;

            _logger.LogWarning(
                "Concurrent checkout for basket {CheckoutKey} lost the race; reusing order {OrderId}",
                checkoutKey, winner.Id);

            // A declared winner never gets an invoice, so there is nothing to wait for; the order is
            // the answer on its own.
            var withInvoice = CheckoutRoutes.Of(winner) == SettlementRoute.Declared
                ? winner
                : await CheckoutGuard.WaitForInvoiceAsync(_context, winner.Id, cancellationToken) ?? winner;

            return new CreateCheckoutResponse
            {
                OrderId = withInvoice.Id,
                Status = withInvoice.Status.ToString(),
                SettlementRoute = CheckoutRoutes.Of(withInvoice).ToString(),
                MonobankInvoiceId = withInvoice.MonobankInvoiceId,
                PaymentUrl = withInvoice.MonobankPaymentUrl
            };
        }

        // Planning #867 trigger 2: the money can arrive before the order does. When what this order
        // still owes is sitting in this customer's balance, the balance pays for it now — out of one
        // transfer or across several — and the acquirer is not asked for an invoice the customer would
        // then pay a second time for the same fuel.
        var held = await _heldMoney.HandleAsync(
            new SettleFromHeldMoneyCommand { OrderId = order.Id, Because = "the checkout" },
            cancellationToken);

        if (held.Settled)
        {
            _logger.LogInformation(
                "Checkout for basket {CheckoutKey} settled on held money ({PaymentIds}); no invoice requested",
                checkoutKey, string.Join(", ", held.PaymentIds));

            _metrics.OrderCreated(order.LegalEntityId.HasValue);

            return new CreateCheckoutResponse
            {
                OrderId = order.Id,
                Status = OrderStatus.PendingFulfillment.ToString(),
                SettlementRoute = route.ToString(),
                AlreadyPaid = true
            };
        }

        // The declared route ends here, and what it ends with is not a failure. The order exists, the
        // acquirer was never asked, and the money it waits for is the settlement channel's: either it
        // is already held and the next declaration names it, or it has not arrived yet and the order
        // is the thing it will arrive against. Telling the customer this is the whole reason the
        // response carries a route - an order with no payment URL is otherwise the same shape as a
        // payment that failed to open.
        //
        // Nothing here guesses at an amount. The order keeps its price, and whether the money on hand
        // covers it is the settle's question, asked above at the checkout and again when the next
        // declaration names this customer (planning #889: held money is a balance that orders spend,
        // and money short of the price leaves the order waiting rather than half-paid).
        if (route == SettlementRoute.Declared)
        {
            _metrics.OrderCreated(order.LegalEntityId.HasValue);

            _logger.LogInformation(
                "Checkout for basket {CheckoutKey} written on the declared route; the acquirer was not asked and "
                 + "order {OrderId} waits on the settlement channel",
                checkoutKey, order.Id);

            return new CreateCheckoutResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                SettlementRoute = route.ToString()
            };
        }

        // Past the declared route's answer, so this request is on the acquirer one - and that is the
        // route the resolution above was asked about.
        var acquirerMerchant = merchant!.Value;

        var invoiceStopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Declared outside the try so the response - specifically AppUrl - is still reachable from
        // the return below. It stays null when invoice creation threw.
        MonobankInvoiceResponse? invoiceResponse = null;

        try
        {
            var monobankClient = _monobankClientFactory.ForMerchant(acquirerMerchant);

            // The receipt's goods lines. Always sent: Monobank registers the fiscal receipt for us
            // and can only print what we declare, and the hryvnia link is switched on in the bank's
            // web cabinet without warning us. Declaring always means activating it is not a
            // launch-day event.
            var invoiceRequest = new MonobankInvoiceRequest
            {
                // Monobank is the one place amounts must be kopecks.
                Amount = Money.ForMonobank(order.Price),
                MerchantPaymentInfo = $"FuelFlow Order {order.Id}",
                Destination = MonobankClient.DefaultDestination,
                Items = MonobankBasketBuilder.Build(order.LineItems.Select(li => (
                    li.FuelTypeId, li.Liters, li.Quantity, li.UnitPrice, li.LineTotal))),
                RedirectUrl = _monobankOptions.RedirectUrl,
                WebhookUrl = _monobankOptions.WebhookUrl
            };

            invoiceResponse = await monobankClient.CreateInvoiceAsync(invoiceRequest, cancellationToken);

            _metrics.MonobankInvoiceCreated(invoiceStopwatch.Elapsed.TotalMilliseconds);

            order.MonobankInvoiceId = invoiceResponse.InvoiceId;
            order.MonobankPaymentUrl = invoiceResponse.PageUrl;

            // The payment gets its own row rather than living on the order's four columns. This is
            // the only point at which an invoice is born, so it is the only point that can write
            // down that it exists - and until a Payment row exists, a payment link re-issued later
            // would have nowhere to put a second attempt.
            _context.Payments.Add(PaymentFactory.CreateCapture(
                order, acquirerMerchant, invoiceResponse.InvoiceId, SystemClock.UtcNow));

            _logger.LogInformation(
                "Monobank invoice created on {Merchant} merchant for order {OrderId}: {InvoiceId}, payment URL: {PaymentUrl}",
                acquirerMerchant,
                order.Id,
                invoiceResponse.InvoiceId,
                invoiceResponse.PageUrl);
        }
        catch (Exception ex)
        {
            // The failure is NOT swallowed. The order is already written - it was, on purpose,
            // committed before the acquirer was asked - so the sale is on record and the basket is
            // given back below, which makes the customer's next tap a fresh attempt. But a checkout
            // that cannot be paid is not a successful checkout: the customer has no link to pay,
            // and the operator has to hear that the payment setup broke.
            //
            // Answering 200 with an order and no invoice (what this used to do) bought nobody
            // anything: the app turns a missing payment link into an error of its own, and the
            // metric was the only trace a payment outage left behind.
            _metrics.MonobankInvoiceFailed(ex.GetType().Name);
            _logger.LogError(ex, "Failed to create Monobank invoice for order {OrderId}", order.Id);

            // The invoice never came, so this attempt is over - and an attempt that is over must
            // not hold the basket against the customer's next try. It is also over as a sale: no
            // invoice means nothing can ever arrive against this order, so it is cancelled rather
            // than left reading as "awaiting payment" for ever (see CheckoutGuard.AbandonAsync).
            await CheckoutGuard.AbandonAsync(_context, order, SystemClock.UtcNow, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            throw;
        }

        // The invoice, written onto the order that is already there. A second attempt for this
        // basket is refused by the index above and never reaches this point, so this is the one
        // and only invoice for this basket.
        await _context.SaveChangesAsync(cancellationToken);

        _metrics.OrderCreated(order.LegalEntityId.HasValue);

        _logger.LogInformation("Order {OrderId} created successfully", order.Id);

        return new CreateCheckoutResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            SettlementRoute = route.ToString(),
            MonobankInvoiceId = order.MonobankInvoiceId,
            PaymentUrl = order.MonobankPaymentUrl,
            // Not persisted on the order: AppUrl is an opaque mbnk.app redirector that cannot be
            // reconstructed, so it is returned once, here. The app uses it immediately for the
            // "Pay via mono" button; re-opening payment later falls back to PaymentUrl, which is
            // persisted and renders in the same in-app screen.
            AppUrl = invoiceResponse?.AppUrl
        };
    }
}
