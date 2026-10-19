using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
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

    public CreateCheckoutCommandHandler(
        ApplicationDbContext context,
        IMonobankClientFactory monobankClientFactory,
        IMonobankMerchantResolver merchantResolver,
        IOptions<MonobankOptions> monobankOptions,
        ILogger<CreateCheckoutCommandHandler> logger,
        FuelFlowMetrics metrics,
        NotificationDispatcher notifications,
        PackageStockAvailability stock)
    {
        _context = context;
        _monobankClientFactory = monobankClientFactory;
        _merchantResolver = merchantResolver;
        _monobankOptions = monobankOptions.Value;
        _logger = logger;
        _metrics = metrics;
        _notifications = notifications;
        _stock = stock;
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
            .IgnoreQueryFilters()
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

        // Pool membership is resolved once, by the same helper the storefront uses, and reused for the
        // package pick and the stock check below. Reading user.IsQaAccount directly - which is what
        // this did - is a THIRD definition of "is this a QA account": the storefront ORs in the
        // configured QA phone, and MonobankMerchantResolver ORs in an allowlist. So a QA account
        // deleted and re-signed-up (a fresh row for the same phone, no flag) saw the QA catalogue on
        // the shopfront and was then stock-checked against the real pool, on a catalogue the app had
        // just told it was available.
        var isTestAccount = await _stock.IsTestAccountAsync(user.Id, cancellationToken);

        // Resolved through the shared helper, not by a local FirstOrDefaultAsync: a triple is not
        // unique, because every real package has a QA mirror on the same Brand/fuel/nominal, so an
        // unordered pick returns whichever row Postgres produced first. See PackageStockAvailability.
        var package = await _stock.FindPackageAsync(
            isTestAccount, command.BrandId.Value, command.FuelTypeId, command.Liters, cancellationToken);

        if (package == null)
        {
            throw new ArgumentException(
                $"No pricing found for fuel type {command.FuelTypeId} at Brand {command.BrandId} for {command.Liters}L");
        }

        // Same gate as the bulk path, and for the same reason: never mint an invoice for fuel there is no
        // stock to deliver. Checked before the below-cost block so an empty shelf does not fire an
        // operator alert about a sale that was never going to happen. Answers about the same pool
        // the package above was taken from.
        if (!await _stock.IsInStockAsync(isTestAccount, package.BrandSlug, command.FuelTypeId, command.Liters, cancellationToken))
        {
            throw new PackageOutOfStockException(package.BrandSlug, command.FuelTypeId, command.Liters);
        }

        // Slice-3 hard block (safety net at checkout, mirroring the operator write path): refuse a
        // sale whose recomputed price/╨╗ sits below blended cost unless this supplier+fuel is opted in.
        if (!fuelTypeEntity.AllowBelowCost
            && package.SupplierPricePerLiter is { } cost
            && package.MarginUahPerLiter is { } profit
            && FuelPricing.IsBelowCost(cost, profit, package.PumpPricePerLiter, package.MinDiscountPerLiter ?? 0m))
        {
            // A customer is standing at the payment screen being refused, and nothing tells anyone.
            // The pricing alert only fired on a manager saving a price or costing a batch, so a fuel
            // that drifted below cost blocked sales silently. Throttled per fuel inside the dispatcher,
            // so a customer retrying the same checkout does not flood the chat.
            await _notifications.BelowCostAsync(
                package.BrandSlug, fuelTypeEntity.Name, cost, profit, deliberate: false, cancellationToken);
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

        // The key names WHAT was bought and by whom; the window is the `CreatedAtUtc` filter below.
// The minute of the clock used to be in here, rounded down to a five-minute bucket, which made
        // the window discontinuous: a retry one millisecond before the bucket rolls over produced a
        // different key, the dedupe missed, and the customer was charged twice for one tap. The
        // filter below already asks "within the last hour", so nothing here needs the clock at all.
        var bucketKey = $"{command.UserId}:{command.BrandId}:{command.FuelTypeId}:{command.Liters}:{command.Quantity}";

        // Dedupe only while the previous attempt is still awaiting payment:
        // retries and double-taps reuse the same invoice, but once an order is
        // settled a legitimate repeat purchase in the same bucket gets a fresh one.
        var existingOrder = await _context.Orders
            .Where(o => o.Status == OrderStatus.PendingPayment
                        && o.IdempotencyKey!.StartsWith(bucketKey)
                        && o.CreatedAtUtc > DateTime.UtcNow.AddHours(-1))
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingOrder != null && !string.IsNullOrEmpty(existingOrder.MonobankPaymentUrl))
        {
            _logger.LogWarning("Duplicate order creation attempt detected for key {IdempotencyKey}", bucketKey);
            return new CreateCheckoutResponse
            {
                OrderId = existingOrder.Id,
                Status = existingOrder.Status.ToString(),
                MonobankInvoiceId = existingOrder.MonobankInvoiceId,
                PaymentUrl = existingOrder.MonobankPaymentUrl
            };
        }

        // Per-attempt suffix keeps the unique index satisfied when the bucket
        // already holds settled orders from the same user.
        var idempotencyKey = $"{bucketKey}:{Guid.NewGuid():N}";

        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            UserId = command.UserId!.Value,
            LegalEntityId = command.LegalEntityId,
            Price = lineTotal,
            Status = OrderStatus.PendingPayment,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        order.LineItems.Add(new OrderLineItem
        {
            Id = Guid.CreateVersion7(),
            OrderId = order.Id,
            Brand = package.BrandSlug,
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
        var merchant = _merchantResolver.Resolve(user.PhoneNumber, user.IsQaAccount);
        order.MonobankMerchant = merchant;

        var invoiceStopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Declared outside the try so the response - specifically AppUrl - is still reachable from
        // the return below. It stays null when invoice creation threw.
        MonobankInvoiceResponse? invoiceResponse = null;

        try
        {
            var monobankClient = _monobankClientFactory.ForMerchant(merchant);

            // The receipt's goods lines. Always sent: Monobank registers the fiscal receipt for us
            // and can only print what we declare, and the ╨ƒ╨á╨á╨₧ link is switched on in the bank's web
            // cabinet without warning us. Declaring always means activating it is not a launch-day
            // event.
            var fuelNames = await MonobankBasketBuilder.FuelNamesAsync(
                _context, order.LineItems.Select(li => li.FuelTypeId), cancellationToken);

            var invoiceRequest = new MonobankInvoiceRequest
            {
                // Monobank is the one place amounts must be kopecks.
                Amount = Money.ForMonobank(order.Price),
                MerchantPaymentInfo = $"FuelFlow Order {order.Id}",
                Destination = "╨ƒ╨░╨╗╨╕╨▓╨╛",
                Items = MonobankBasketBuilder.Build(order.LineItems.Select(li => (
                    li.Brand, li.FuelTypeId,
                    fuelNames.GetValueOrDefault(li.FuelTypeId),
                    li.Liters, li.Quantity, li.UnitPrice, li.LineTotal))),
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
                order, merchant, invoiceResponse.InvoiceId, DateTime.UtcNow));

            _logger.LogInformation(
                "Monobank invoice created on {Merchant} merchant for order {OrderId}: {InvoiceId}, payment URL: {PaymentUrl}",
                merchant,
                order.Id,
                invoiceResponse.InvoiceId,
                invoiceResponse.PageUrl);
        }
        catch (Exception ex)
        {
            // The exception is deliberately swallowed so checkout still returns an order,
            // which means this metric is the only lasting signal that payment setup broke.
            _metrics.MonobankInvoiceFailed(ex.GetType().Name);
            _logger.LogError(ex, "Failed to create Monobank invoice for order {OrderId}", order.Id);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _metrics.OrderCreated(order.LegalEntityId.HasValue);

        _logger.LogInformation("Order {OrderId} created successfully", order.Id);

        return new CreateCheckoutResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
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
