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
    private readonly IMonobankClientFactory _monobankClientFactory;
    private readonly IMonobankMerchantResolver _merchantResolver;
    private readonly MonobankOptions _monobankOptions;
    private readonly RuntimeSettingsService _settings;
    private readonly NotificationDispatcher _notifications;
    private readonly ILogger<RenewalCheckoutCommandHandler> _logger;

    public RenewalCheckoutCommandHandler(
        ApplicationDbContext context,
        IMonobankClientFactory monobankClientFactory,
        IMonobankMerchantResolver merchantResolver,
        IOptions<MonobankOptions> monobankOptions,
        RuntimeSettingsService settings,
        NotificationDispatcher notifications,
        ILogger<RenewalCheckoutCommandHandler> logger)
    {
        _context = context;
        _monobankClientFactory = monobankClientFactory;
        _merchantResolver = merchantResolver;
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

            var tier = config.Tier(term);
            if (tier is null || !tier.IsOfferable)
                throw new VoucherRenewalException("tier_unavailable", $"The selected term '{item.TermCode}' is not available right now.");

            var offeredTerms = VoucherRenewalTerms.All
                .Where(t => config.Tier(t) is { IsOfferable: true })
                .ToList();

            // Renewable status, inside the trigger window, and a branch that still has room to grow in.
            if (!VoucherRenewalEligibility.TryResolveBranch(
                    source.Status,
                    source.CustomerExpirationDate,
                    source.ProviderExpirationDate,
                    today,
                    config.TriggerThresholdDays,
                    offeredTerms,
                    out var branch))
                throw new VoucherRenewalException("not_renewable",
                    "One of the selected vouchers cannot be renewed (still valid for a while, spent, or blocked).");

            // The ceiling, re-checked here rather than trusting the quote. A still-valid voucher whose
            // supplier term cannot absorb this term has nothing to extend into - it must be replaced, and
            // the customer has to be told before they pay, not after.
            if (branch == VoucherRenewalBranch.Extend &&
                !VoucherRenewalEligibility.CanExtend(source.CustomerExpirationDate, source.ProviderExpirationDate, term))
                throw new VoucherRenewalException("provider_term_exhausted",
                    $"The '{item.TermCode}' term is longer than this voucher's remaining supplier validity.");

            var lineAmount = VoucherRenewalPricing.LineAmountUah(source.Liters, tier.RatePerLiterUah);
            if (lineAmount <= 0)
                throw new VoucherRenewalException("invalid_price", "Renewal price could not be computed for one of the vouchers.");

            resolved.Add(new ResolvedLine(source, term, branch, lineAmount));
        }

        // Replace lines each need a DISTINCT in-stock voucher at offer time — a can-this-batch-be-
        // filled gate, not a hold (no payment has happened). The atomic claim runs post-payment in
        // FulfillmentService; if stock depletes in between, that path auto-refunds the missing line.
        var reservedStock = new List<Guid>();
        var candidateCosts = new Dictionary<Guid, decimal?>();
        foreach (var line in resolved.Where(r => r.Branch == VoucherRenewalBranch.Replace))
        {
            var promisedExpiration = VoucherRenewalEligibility.PromisedExpirationForReplace(
                today, line.Source.CustomerExpirationDate, line.Term);
            var candidate = await _context.FuelVouchers
                .AsNoTracking()
                .Where(v => v.Status == VoucherStatus.Available
                         && v.Provider.ToLower() == line.Source.Provider.ToLower()
                         && v.FuelTypeId == line.Source.FuelTypeId
                         && v.Liters == line.Source.Liters
                         && v.ProviderExpirationDate >= promisedExpiration
                         && !reservedStock.Contains(v.Id))
                .OrderBy(v => v.ProviderExpirationDate)
                .Select(v => new { v.Id, v.CostPerLiter })
                .FirstOrDefaultAsync(cancellationToken);

            // FirstOrDefaultAsync over an anonymous projection yields null when nothing matches — not a
            // default-initialised struct — so this has to be a null check.
            if (candidate is null)
            {
                var fuelName = await ResolveFuelNameAsync(line.Source, cancellationToken);
                await _notifications.VoucherStockLowAsync(line.Source.Provider, fuelName, 0, 1, cancellationToken);
                throw new VoucherRenewalException("no_stock", $"No replacement voucher is currently in stock for {fuelName}.");
            }

            reservedStock.Add(candidate.Id);
            // Same ordering and filters as the post-payment claim in FulfillmentService, so this is the
            // voucher that will actually be handed over — and therefore the one whose cost decides.
            candidateCosts[line.Source.Id] = candidate.CostPerLiter;
        }

        // A replace issues stock, so the tier rate is a sale price that must clear what the stock costs,
        // net of the voucher the customer hands back to us. Judging the fee alone refused renewals that
        // made money, and judged against the whole pool's average it judged a voucher nobody would get.
        //
        // The replacement's cost is the candidate's own. The old blend existed because the exact voucher
        // was supposedly unknown until payment — but the candidate IS the one the claim reaches for
        // first, and vouchers of one fuel can carry different costs (different purchase terms), so an
        // average of the pool describes neither of them. The blend survives only as the fallback for a
        // candidate with no recorded cost, where it still beats having no verdict at all.
        foreach (var line in resolved.Where(r => r.Branch == VoucherRenewalBranch.Replace))
        {
            var replacementCost = candidateCosts.GetValueOrDefault(line.Source.Id);

            if (replacementCost is not { } priced || priced <= 0m)
            {
                var pool = (await _context.FuelVouchers
                        .AsNoTracking()
                        .Where(v => v.Status == VoucherStatus.Available
                                 && v.Provider.ToLower() == line.Source.Provider.ToLower()
                                 && v.FuelTypeId == line.Source.FuelTypeId
                                 && v.Liters == line.Source.Liters
                                 && v.CostPerLiter != null)
                        .Select(v => new { v.CostPerLiter, v.Liters })
                        .ToListAsync(cancellationToken))
                    .Where(p => p.CostPerLiter is { } c && c > 0m)
                    .ToList();

                var poolLiters = pool.Sum(p => p.Liters);
                if (poolLiters <= 0m) continue;   // nothing priced: nothing to judge against

                replacementCost = pool.Sum(p => p.CostPerLiter!.Value * p.Liters) / poolLiters;
            }

            // line.Source is the customer's own voucher, coming back to us, at what we paid for it.
            if (RenewalMargin.ForReplacement(
                    line.LineAmount, line.Source.Liters, replacementCost, line.Source.CostPerLiter)
                == RenewalMarginVerdict.BelowCost)
            {
                var fuelType = await _context.FuelTypes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(f => f.Id == line.Source.FuelTypeId, cancellationToken);

                if (fuelType?.AllowBelowCost != true)
                {
                    var shortfall = RenewalMargin.ShortfallUah(
                        line.LineAmount, line.Source.Liters, replacementCost, line.Source.CostPerLiter);
                    throw new VoucherRenewalException(
                        "below_cost",
                        $"Renewal of the '{line.Term.Code()}' term is below the replacement voucher's cost "
                        + $"by {shortfall:F2} UAH. Enable the below-cost opt-in for this fuel if deliberate.");
                }
            }
        }

        decimal totalUah;
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

        // Resolve merchant for this account. Outside the try below on purpose: a QA account routed
        // to an unconfigured sandbox is refused outright rather than degraded into an order with
        // no invoice, and never quietly pointed at the live merchant.
        var merchant = _merchantResolver.Resolve(user.PhoneNumber, user.IsQaAccount);

        MonobankInvoiceResponse invoiceResponse;
        try
        {
            var monobankClient = _monobankClientFactory.ForMerchant(merchant);

            invoiceResponse = await monobankClient.CreateInvoiceAsync(
                new MonobankInvoiceRequest
                {
                    Amount = Money.ToKopecksLong(totalUah),
                    MerchantPaymentInfo = "FuelFlow Voucher Renewal",
                    RedirectUrl = _monobankOptions.RedirectUrl,
                    WebhookUrl = _monobankOptions.WebhookUrl
                }, cancellationToken);

            _logger.LogInformation(
                "Monobank invoice created on {Merchant} merchant for renewal: {InvoiceId}",
                merchant, invoiceResponse.InvoiceId);
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
            Kind = OrderKind.Renewal,
            Status = OrderStatus.PendingPayment,
            IdempotencyKey = idempotencyKey,
            MonobankInvoiceId = invoiceResponse.InvoiceId,
            MonobankPaymentUrl = invoiceResponse.PageUrl,
            MonobankMerchant = merchant,
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
                // OriginalLineTotal left null: a renewal fee has no pump-price reference, so it
                // contributes no "saving vs pump" to the customer savings report.
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
                AmountPaid = line.LineAmount,
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
            // Not persisted on the order - AppUrl is an opaque mbnk.app redirector that cannot be
            // reconstructed later, so it is returned once here. See CreateCheckoutCommandHandler.
            AppUrl = invoiceResponse.AppUrl,
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

    private sealed record ResolvedLine(FuelVoucher Source, VoucherRenewalTerm Term, VoucherRenewalBranch Branch, decimal LineAmount);
}

