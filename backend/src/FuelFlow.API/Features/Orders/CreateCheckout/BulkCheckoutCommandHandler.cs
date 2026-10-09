using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.Terms;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.Features.Orders.CreateCheckout;

public sealed class BulkCheckoutCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IMonobankClient _monobankClient;
    private readonly RuntimeSettingsService _runtimeSettings;
    private readonly MonobankOptions _monobankOptions;
    private readonly ILogger<BulkCheckoutCommandHandler> _logger;

public BulkCheckoutCommandHandler(
        ApplicationDbContext context,
        IMonobankClient monobankClient,
        IOptions<MonobankOptions> monobankOptions,
        RuntimeSettingsService runtimeSettings,
        ILogger<BulkCheckoutCommandHandler> logger)
    {
        _context = context;
        _monobankClient = monobankClient;
        _runtimeSettings = runtimeSettings;
        _monobankOptions = monobankOptions.Value;
        _logger = logger;
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

        var stationIds = command.Items.Select(i => i.StationId!).Distinct().ToList();
        var fuelTypeIds = command.Items.Select(i => i.FuelTypeId).ToList();

        var fuelTypes = await _context.FuelTypes
            .Where(f => stationIds.Contains(f.StationId) && fuelTypeIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        var packages = await _context.FuelPackages
            .Where(p => stationIds.Contains(p.StationId) && fuelTypeIds.Contains(p.FuelTypeId))
            .ToListAsync(cancellationToken);

        // Inactive accounts keep a read-only session but cannot pay.
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == command.UserId.Value, cancellationToken);

        if (user == null || !user.IsActive || user.IsDeleted)
            throw new AccountInactiveException();

        var itemPricing = new List<(CheckoutItem Item, decimal UnitPrice, decimal LineTotal, decimal OriginalLineTotal, string? TermCode)>();
        var totalPrice = 0m;

        // Term-sale ladder, loaded once. Off by default, in which case every line below keeps the
        // undiscounted full-term behaviour exactly as before.
        var termConfig = await _runtimeSettings.GetVoucherTermConfigAsync(cancellationToken);

        foreach (var item in command.Items)
        {
            var fuelType = fuelTypes.FirstOrDefault(f =>
                f.Id == item.FuelTypeId && f.StationId == item.StationId);

            if (fuelType == null)
                throw new ArgumentException(
                    $"Invalid fuel type ID: {item.FuelTypeId} for station {item.StationId}");

            var package = packages.FirstOrDefault(p =>
                p.StationId == item.StationId &&
                p.FuelTypeId == item.FuelTypeId &&
                p.Liters == item.Liters);

            if (package == null)
                throw new ArgumentException(
                    $"No pricing found for fuel type {item.FuelTypeId} at station {item.StationId} for {item.Liters}L");

            var (discountPerLiter, termCode) = ResolveTerm(item, termConfig, command.UserId.Value);

// Slice-3 hard block (safety net at checkout): refuse a below-cost line unless this
            // supplier+fuel is opted in. One blocked item fails the whole bulk order.
            // Evaluated against the DISCOUNTED figure: FuelPricing.IsBelowCost recomputes the price
            // from cost + margin and would never see a term discount, letting a generous tier sell
            // under cost while the guard reported the line was fine.
            if (!fuelType.AllowBelowCost && ServerPricing.IsBelowCost(package, discountPerLiter))
            {
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
                var bestStockExpiration = await TermStockCoverage.BestStockExpirationAsync(
                    _context, item.StationId!, item.FuelTypeId, item.Liters, cancellationToken);

                if (!TermStockCoverage.CanHonour(bestStockExpiration, DateOnly.FromDateTime(DateTime.UtcNow), purchasedTerm))
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

            itemPricing.Add((item, unitPrice, lineTotal,
                ServerPricing.OriginalLineTotal(package, item.Liters, unitPrice, item.Quantity), termCode));
        }

        // Idempotency (mirrors CreateCheckoutCommandHandler's single-item bucket dedup): while a
        // previous attempt for the SAME cart is still awaiting payment, collapse repeats onto that
        // one order + invoice instead of double-charging. This closes #16 — the mobile client's
        // fetchWithRetry auto-resends the identical /bulk POST on timeout/5xx/network with no user
        // action; those retries land in the same bucket and reuse the live invoice. Once an order
        // settles (leaves PendingPayment), a legitimate repeat of the same cart gets a fresh invoice.
        // Must run BEFORE invoice creation so a duplicate never mints a second Monobank invoice.
        // The cart is hashed to a fixed-length digest so the key fits the 150-char column no matter
        // how many lines the cart has, and item order is normalized so the same cart always keys alike.
        var roundedMinute = (DateTime.UtcNow.Minute / 5) * 5;
        var cartSignature = string.Join("|", command.Items
            .Select(i => $"{i.StationId}:{i.FuelTypeId}:{i.Liters}:{i.Quantity}:{i.TermCode ?? "full"}")
            .OrderBy(s => s, StringComparer.Ordinal));
        var cartDigest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(cartSignature)))[..32];
        var bucketKey = $"{command.UserId!.Value:N}:{DateTime.UtcNow:yyyyMMddHH}{roundedMinute:D2}:{cartDigest}";

        var existingOrder = await _context.Orders
            .Where(o => o.Status == OrderStatus.PendingPayment
                        && o.IdempotencyKey!.StartsWith(bucketKey)
                        && o.CreatedAtUtc > DateTime.UtcNow.AddHours(-1))
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingOrder != null && !string.IsNullOrEmpty(existingOrder.MonobankPaymentUrl))
        {
            _logger.LogWarning(
                "Duplicate bulk checkout; reusing pending order {OrderId} / invoice {InvoiceId}",
                existingOrder.Id, existingOrder.MonobankInvoiceId);

            return new BulkCheckoutResponse
            {
                OrderIds = [existingOrder.Id],
                MonobankInvoiceId = existingOrder.MonobankInvoiceId,
                PaymentUrl = existingOrder.MonobankPaymentUrl
            };
        }

        // Per-attempt suffix keeps the unique index (idempotency_key) satisfied when this bucket
        // already holds settled orders, so a fresh purchase of the same cart never collides.
        var idempotencyKey = $"{bucketKey}:{Guid.NewGuid():N}";

        MonobankInvoiceResponse invoiceResponse;
        try
        {
            invoiceResponse = await _monobankClient.CreateInvoiceAsync(
                new MonobankInvoiceRequest
                {
                    // Monobank is the one place amounts must be kopecks.
                    Amount = Money.ToKopecksLong(totalPrice),
                    MerchantPaymentInfo = $"FuelFlow Bundle",
                    RedirectUrl = _monobankOptions.RedirectUrl,
                    WebhookUrl = _monobankOptions.WebhookUrl
                }, cancellationToken);

            _logger.LogInformation(
                "Monobank invoice created for bundle: {InvoiceId}",
                invoiceResponse.InvoiceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Monobank invoice for bundle");
            throw;
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId!.Value,
            LegalEntityId = command.LegalEntityId,
            Price = totalPrice,
            Status = OrderStatus.PendingPayment,
            IdempotencyKey = idempotencyKey,
            MonobankInvoiceId = invoiceResponse.InvoiceId,
            MonobankPaymentUrl = invoiceResponse.PageUrl,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        foreach (var (item, unitPrice, lineTotal, originalLineTotal, termCode) in itemPricing)
        {
            order.LineItems.Add(new OrderLineItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Provider = item.StationId!,
                FuelTypeId = item.FuelTypeId,
                Liters = item.Liters,
                Quantity = item.Quantity,
UnitPrice = unitPrice,
                LineTotal = lineTotal,
                OriginalLineTotal = originalLineTotal,
                TermCode = termCode
            });
        }

        _context.Orders.Add(order);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Bulk checkout created with {LineItemCount} line items, order {OrderId}, invoice {InvoiceId}",
            order.LineItems.Count, order.Id, invoiceResponse.InvoiceId);

return new BulkCheckoutResponse
        {
            OrderIds = [order.Id],
            MonobankInvoiceId = invoiceResponse.InvoiceId,
            PaymentUrl = invoiceResponse.PageUrl,
            // Not persisted on the order - AppUrl is an opaque mbnk.app redirector that cannot be
            // reconstructed later, so it is returned once here. See CreateCheckoutCommandHandler.
            AppUrl = invoiceResponse.AppUrl
        };
    }

    /// <summary>
    /// Resolves the term a line was bought on into the discount to apply and the term code to freeze.
    /// Returns <c>(0, null)</c> — today's behaviour, the voucher's full remaining life at the
    /// undiscounted price — when no term was requested or the feature is off.
    /// </summary>
    /// <remarks>
    /// Fail-safe in the direction that costs nothing: the feature off, an absent term, an unparseable
    /// term, or a tier the manager has not configured all fall back to the full term rather than
    /// rejecting the sale. A customer must never be unable to buy fuel over a settings problem. Stock
    /// sufficiency is a separate gate at fulfilment, where the voucher is actually claimed.
    /// </remarks>
    private (decimal DiscountPerLiter, string? TermCode) ResolveTerm(
        CheckoutItem item, VoucherTermConfig config, Guid userId)
    {
        if (!config.Enabled || string.IsNullOrWhiteSpace(item.TermCode))
            return (0m, null);

        if (!VoucherRenewalTerms.TryFromCode(item.TermCode, out var term))
        {
            _logger.LogWarning(
                "Ignoring unknown purchase term {TermCode} for user {UserId}; selling the full term instead",
                item.TermCode, userId);
            return (0m, null);
        }

        var tier = config.Tier(term);
        if (tier is null || !tier.IsOfferable)
        {
            _logger.LogWarning(
                "Purchase term {TermCode} is not configured for user {UserId}; selling the full term instead",
                item.TermCode, userId);
            return (0m, null);
        }

        return (tier.DiscountPerLiterUah, term.Code());
    }
}
