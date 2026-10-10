using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
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

    public CreateCheckoutCommandHandler(
        ApplicationDbContext context,
        IMonobankClientFactory monobankClientFactory,
        IMonobankMerchantResolver merchantResolver,
        IOptions<MonobankOptions> monobankOptions,
        ILogger<CreateCheckoutCommandHandler> logger,
        FuelFlowMetrics metrics,
        NotificationDispatcher notifications)
    {
        _context = context;
        _monobankClientFactory = monobankClientFactory;
        _merchantResolver = merchantResolver;
        _monobankOptions = monobankOptions.Value;
        _logger = logger;
        _metrics = metrics;
        _notifications = notifications;
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

        if (string.IsNullOrWhiteSpace(command.StationId))
        {
            throw new ArgumentException("StationId is required", nameof(command));
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
            .FirstOrDefaultAsync(f => f.Id == command.FuelTypeId && f.StationId == command.StationId, cancellationToken);

        if (fuelTypeEntity == null)
        {
            throw new ArgumentException($"Invalid fuel type ID: {command.FuelTypeId} for station {command.StationId}");
        }

        var package = await _context.FuelPackages
            .FirstOrDefaultAsync(p =>
                p.StationId == command.StationId &&
                p.FuelTypeId == command.FuelTypeId &&
                p.Liters == command.Liters, cancellationToken);

        if (package == null)
        {
            throw new ArgumentException(
                $"No pricing found for fuel type {command.FuelTypeId} at station {command.StationId} for {command.Liters}L");
        }

        // Slice-3 hard block (safety net at checkout, mirroring the operator write path): refuse a
        // sale whose recomputed price/л sits below blended cost unless this supplier+fuel is opted in.
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
                command.StationId, fuelTypeEntity.Name, cost, profit, deliberate: false, cancellationToken);
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

        var roundedMinute = (DateTime.UtcNow.Minute / 5) * 5;
        var bucketKey = $"{command.UserId}:{command.StationId}:{command.FuelTypeId}:{command.Liters}:{command.Quantity}:{DateTime.UtcNow:yyyyMMddHH}{roundedMinute:D2}";

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
            Id = Guid.NewGuid(),
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
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Provider = command.StationId!,
            FuelTypeId = command.FuelTypeId,
            Liters = command.Liters,
            Quantity = command.Quantity,
            UnitPrice = unitPrice,
            LineTotal = lineTotal,
            OriginalLineTotal = ServerPricing.OriginalLineTotal(package, command.Liters, unitPrice, command.Quantity)
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

            var invoiceRequest = new MonobankInvoiceRequest
            {
                // Monobank is the one place amounts must be kopecks.
                Amount = Money.ToKopecksLong(order.Price),
                MerchantPaymentInfo = $"FuelFlow Order {order.Id}",
                RedirectUrl = _monobankOptions.RedirectUrl,
                WebhookUrl = _monobankOptions.WebhookUrl
            };

            invoiceResponse = await monobankClient.CreateInvoiceAsync(invoiceRequest, cancellationToken);

            _metrics.MonobankInvoiceCreated(invoiceStopwatch.Elapsed.TotalMilliseconds);

            order.MonobankInvoiceId = invoiceResponse.InvoiceId;
            order.MonobankPaymentUrl = invoiceResponse.PageUrl;

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