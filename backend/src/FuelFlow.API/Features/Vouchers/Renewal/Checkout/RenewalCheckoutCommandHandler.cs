using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.Features.Vouchers.Renewal.Checkout;

/// <summary>
/// Prices and creates a paid renewal/replacement checkout for a batch of the caller's vouchers, and
/// mints one Monobank invoice for the lot. Nothing mutates a voucher here — every state change waits
/// for payment success and happens in FulfillmentService. This handler only validates eligibility,
/// prices each line, records the intent as an Order + line items + <see cref="VoucherRenewalItem"/>
/// rows, and hands back the payment URL.
/// </summary>
public sealed class RenewalCheckoutCommandHandler
{
    /// <summary>Matches the buy-fuel bulk cap; keeps the invoice and the batch bounded.</summary>
    private const int MaxItems = 50;

    private readonly ApplicationDbContext _context;
    private readonly IMonobankClient _monobankClient;
    private readonly MonobankOptions _monobankOptions;
    private readonly RuntimeSettingsService _settings;
    private readonly NotificationDispatcher _notifications;
    private readonly ILogger<RenewalCheckoutCommandHandler> _logger;

    public RenewalCheckoutCommandHandler(
        ApplicationDbContext context,
        IMonobankClient monobankClient,
        IOptions<MonobankOptions> monobankOptions,
        RuntimeSettingsService settings,
        NotificationDispatcher notifications,
        ILogger<RenewalCheckoutCommandHandler> logger)
    {
        _context = context;
        _monobankClient = monobankClient;
        _monobankOptions = monobankOptions.Value;
        _settings = settings;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<RenewalCheckoutResponse> HandleAsync(
        RenewalCheckoutCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.UserId == null || command.UserId == Guid.Empty)
            throw new ArgumentException("UserId is required", nameof(command));

        if (command.Items.Count == 0)
            throw new VoucherRenewalException("empty_batch", "Select at least one voucher to renew.");

        if (command.Items.Count > MaxItems)
            throw new VoucherRenewalException("too_many_items", $"A renewal batch may contain at most {MaxItems} vouchers.");

        var userId = command.UserId.Value;

        // Renewing the same voucher twice in one batch is nonsensical — its expiry moves once.
        if (command.Items.GroupBy(i => i.VoucherId).Any(g => g.Count() > 1))
            throw new VoucherRenewalException("duplicate_voucher", "Each voucher may appear only once in a renewal batch.");

        _logger.LogInformation(
            "Creating renewal checkout for user {UserId} with {ItemCount} vouchers",
            userId, command.Items.Count);

        // Feature gate (fail-safe off): nothing renews until a manager turns it on and prices tiers.
        var config = await _settings.GetVoucherRenewalConfigAsync(cancellationToken);
        if (!config.Enabled)
            throw new VoucherRenewalException("renewal_disabled", "Voucher renewal is not available right now.");

        // Inactive accounts keep a read-only session but cannot pay.
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null || !user.IsActive || user.IsDeleted)
            throw new AccountInactiveException();

        var voucherIds = command.Items.Select(i => i.VoucherId).ToList();
        var sources = await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => voucherIds.Contains(v.Id))
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var resolved = new List<ResolvedLine>();
        foreach (var item in command.Items)
        {
            var source = sources.FirstOrDefault(v => v.Id == item.VoucherId);

            // Ownership: only the customer's own voucher. Soft-deleted vouchers are already excluded
            // by the global !IsDeleted filter, so a missing source also covers a deleted one.
            if (source == null || source.AssignedToUserId != userId)
                throw new VoucherRenewalException("not_your_voucher", "One of the selected vouchers is not available for renewal.");

            if (!VoucherRenewalTerms.TryFromCode(item.TermCode, out var term))
                throw new VoucherRenewalException("unknown_term", $"Unknown renewal term '{item.TermCode}'.");

            // Renewable status AND within the trigger window (already-expired vouchers included).
            if (!VoucherRenewalEligibility.TryResolveBranch(
                    source.Status, source.ExpirationDate, today, config.TriggerThresholdDays, out var branch))
                throw new VoucherRenewalException("not_renewable",
                    "One of the selected vouchers cannot be renewed (still valid for a while, spent, or blocked).");

            var tier = config.Tier(term);
            if (tier is null || !tier.IsOfferable)
                throw new VoucherRenewalException("tier_unavailable", $"The selected term '{item.TermCode}' is not available right now.");

            var lineAmount = VoucherRenewalPricing.LineAmountUah(source.Liters, tier.RatePerLiterUah);
            if (lineAmount <= 0)
                throw new VoucherRenewalException("invalid_price", "Renewal price could not be computed for one of the vouchers.");

            resolved.Add(new ResolvedLine(source, term, branch, lineAmount));
        }

        // Replace lines each need a DISTINCT in-stock voucher at offer time — a can-this-batch-be-
        // filled gate, not a hold (no payment has happened). The atomic claim runs post-payment in
        // FulfillmentService; if stock depletes in between, that path auto-refunds the missing line.
        var reservedStock = new List<Guid>();
        foreach (var line in resolved.Where(r => r.Branch == VoucherRenewalBranch.Replace))
        {
            var minExpiration = VoucherRenewalEligibility.MinStockExpirationForReplace(today, line.Term);
            var stockId = await _context.FuelVouchers
                .AsNoTracking()
                .Where(v => v.Status == VoucherStatus.Available
                         && v.Provider.ToLower() == line.Source.Provider.ToLower()
                         && v.FuelTypeId == line.Source.FuelTypeId
                         && v.Liters == line.Source.Liters
                         && v.ExpirationDate >= minExpiration
                         && !reservedStock.Contains(v.Id))
                .OrderBy(v => v.ExpirationDate)
                .Select(v => v.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (stockId == Guid.Empty)
            {
                var fuelName = await ResolveFuelNameAsync(line.Source, cancellationToken);
                await _notifications.VoucherStockLowAsync(line.Source.Provider, fuelName, 0, 1, cancellationToken);
                throw new VoucherRenewalException("no_stock", $"No replacement voucher is currently in stock for {fuelName}.");
            }

            reservedStock.Add(stockId);
        }

        int totalUah;
        try
        {
            totalUah = checked(resolved.Sum(r => r.LineAmount));
        }
        catch (OverflowException)
        {
            throw new VoucherRenewalException("total_too_large", "Renewal total is too large.");
        }

        if (totalUah <= 0)
            throw new VoucherRenewalException("invalid_total", "Renewal total must be greater than zero.");

        // Idempotency (mirrors BulkCheckoutCommandHandler): collapse an accidental resend of the SAME
        // batch onto the live pending order + invoice instead of double-charging. The "renew:" prefix
        // keeps a renewal bucket from ever matching a buy-fuel order's key on the StartsWith probe.
        var roundedMinute = (DateTime.UtcNow.Minute / 5) * 5;
        var cartSignature = string.Join("|", command.Items
            .Select(i => $"{i.VoucherId:N}:{i.TermCode}")
            .OrderBy(s => s, StringComparer.Ordinal));
        var cartDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cartSignature)))[..32];
        var bucketKey = $"renew:{userId:N}:{DateTime.UtcNow:yyyyMMddHH}{roundedMinute:D2}:{cartDigest}";

        var existingOrder = await _context.Orders
            .Where(o => o.Status == OrderStatus.PendingPayment
                        && o.IdempotencyKey!.StartsWith(bucketKey)
                        && o.CreatedAtUtc > DateTime.UtcNow.AddHours(-1))
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingOrder != null && !string.IsNullOrEmpty(existingOrder.MonobankPaymentUrl))
        {
            _logger.LogWarning(
                "Duplicate renewal checkout; reusing pending order {OrderId} / invoice {InvoiceId}",
                existingOrder.Id, existingOrder.MonobankInvoiceId);

            return new RenewalCheckoutResponse
            {
                OrderId = existingOrder.Id,
                MonobankInvoiceId = existingOrder.MonobankInvoiceId,
                PaymentUrl = existingOrder.MonobankPaymentUrl,
                TotalUah = existingOrder.Price
            };
        }

        // Per-attempt suffix keeps the unique idempotency_key index satisfied once this bucket
        // already holds a settled order, so a fresh purchase of the same batch never collides.
        var idempotencyKey = $"{bucketKey}:{Guid.NewGuid():N}";

        MonobankInvoiceResponse invoiceResponse;
        try
        {
            invoiceResponse = await _monobankClient.CreateInvoiceAsync(
                new MonobankInvoiceRequest
                {
                    Amount = Money.ToKopecks(totalUah),
                    MerchantPaymentInfo = "FuelFlow Voucher Renewal",
                    RedirectUrl = _monobankOptions.RedirectUrl,
                    WebhookUrl = _monobankOptions.WebhookUrl
                }, cancellationToken);

            _logger.LogInformation("Monobank invoice created for renewal: {InvoiceId}", invoiceResponse.InvoiceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Monobank invoice for renewal batch");
            throw;
        }

        var now = DateTime.UtcNow;
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            // Left null on purpose: a replacement inherits the SOURCE voucher's legal entity at
            // fulfilment time, so the order need not (and cannot, for a mixed batch) carry one.
            LegalEntityId = null,
            Price = totalUah,
            Status = OrderStatus.PendingPayment,
            IdempotencyKey = idempotencyKey,
            MonobankInvoiceId = invoiceResponse.InvoiceId,
            MonobankPaymentUrl = invoiceResponse.PageUrl,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        foreach (var line in resolved)
        {
            // The line item mirrors the source voucher (provider/fuel/nominal) so the shared refund
            // maths — which groups by Provider/FuelTypeId/Liters and refunds unfulfilled units — works
            // unchanged when a renewal order ends up only partially fulfilled.
            order.LineItems.Add(new OrderLineItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Provider = line.Source.Provider,
                FuelTypeId = line.Source.FuelTypeId,
                Liters = line.Source.Liters,
                Quantity = 1,
                UnitPrice = line.LineAmount,
                LineTotal = line.LineAmount
            });
        }

        _context.Orders.Add(order);

        foreach (var line in resolved)
        {
            _context.VoucherRenewalItems.Add(new VoucherRenewalItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                SourceVoucherId = line.Source.Id,
                TermCode = line.Term.Code(),
                CreatedAtUtc = now
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Renewal checkout created: order {OrderId}, {LineCount} vouchers, invoice {InvoiceId}, total {TotalUah} UAH",
            order.Id, resolved.Count, invoiceResponse.InvoiceId, totalUah);

        return new RenewalCheckoutResponse
        {
            OrderId = order.Id,
            MonobankInvoiceId = invoiceResponse.InvoiceId,
            PaymentUrl = invoiceResponse.PageUrl,
            TotalUah = totalUah
        };
    }

    private async Task<string> ResolveFuelNameAsync(FuelVoucher source, CancellationToken cancellationToken)
    {
        var name = await _context.FuelTypes
            .AsNoTracking()
            .Where(ft => ft.Id == source.FuelTypeId)
            .Select(ft => ft.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var label = string.IsNullOrEmpty(name) ? source.FuelTypeId : name;
        return $"{label} ({source.Provider.ToUpperInvariant()}, {source.Liters:0.##} L)";
    }

    private sealed record ResolvedLine(FuelVoucher Source, VoucherRenewalTerm Term, VoucherRenewalBranch Branch, int LineAmount);
}
