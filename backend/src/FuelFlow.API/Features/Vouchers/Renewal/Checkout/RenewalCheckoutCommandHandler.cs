using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.CreateCheckout;
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

namespace FuelFlow.Features.Vouchers.Renewal.Checkout;

/// <summary>
/// Prices and creates a paid renewal/replacement checkout for a batch of the caller's vouchers, and
/// mints one Monobank invoice for the lot. Nothing mutates a voucher here — every state change waits
/// for payment success and happens in FulfillmentService. This handler only validates eligibility,
/// prices each line, records the intent as an Order + line items (the lines are
/// the renewal record - which voucher, which term, which branch, for how much), and
/// hands back the payment URL.
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
                    source.SupplierExpirationDate,
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
                !VoucherRenewalEligibility.CanExtend(source.CustomerExpirationDate, source.SupplierExpirationDate, term))
                throw new VoucherRenewalException("provider_term_exhausted",
                    $"The '{item.TermCode}' term is longer than this voucher's remaining supplier validity.");

            var lineAmount = VoucherRenewalPricing.LineAmountUah(source.Liters, tier.RatePerLiterUah);
            if (lineAmount <= 0)
                throw new VoucherRenewalException("invalid_price", "Renewal price could not be computed for one of the vouchers.");

            resolved.Add(new ResolvedLine(source, term, branch, lineAmount));
        }

        // One lookup for the whole batch: the fuel types behind the lines. The below-cost guard
        // below asked the database for each one of them in turn.
        var fuelTypesById = await _context.FuelTypes
            .AsNoTracking()
            .Where(f => resolved.Select(r => r.Source.FuelTypeId).Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f, cancellationToken);

        // And one grouped query for the pool blend: the weighted average cost of the priced
        // vouchers of every fuel the batch touches. It was one query per line before.
        var blendBrands = resolved.Select(r => r.Source.Brand).Distinct().ToArray();
        var blendFuelTypeIds = resolved.Select(r => r.Source.FuelTypeId).Distinct().ToArray();
        var blendLiterSizes = resolved.Select(r => r.Source.Liters).Distinct().ToArray();

        var poolBlends = (await (
                from v in _context.FuelVouchers.AsNoTracking()
                where v.Status == VoucherStatus.Available
                      && v.CostPerLiter != null
                      && v.CostPerLiter > 0m
                      && blendBrands.Contains(v.Brand)
                      && blendFuelTypeIds.Contains(v.FuelTypeId)
                      && blendLiterSizes.Contains(v.Liters)
                group v by new { v.Brand, v.FuelTypeId, v.Liters } into g
                select new
                {
                    g.Key.Brand,
                    g.Key.FuelTypeId,
                    g.Key.Liters,
                    PoolLiters = g.Sum(v => v.Liters),
                    Cost = g.Sum(v => v.CostPerLiter * v.Liters)
                })
                .ToListAsync(cancellationToken))
            .ToDictionary(
                b => (b.Brand, b.FuelTypeId, b.Liters),
                b => (b.PoolLiters, b.Cost));

        // Replace lines each need a DISTINCT in-stock voucher at offer time — a can-this-batch-be-
        // filled gate, not a hold (no payment has happened). The atomic claim runs post-payment in
        // FulfillmentService; if stock depletes in between, that path auto-refunds the missing line.
        var reservedStock = new List<Guid>();
        var candidateCosts = new Dictionary<Guid, decimal?>();
        foreach (var line in resolved.Where(r => r.Branch == VoucherRenewalBranch.Replace))
        {
            var promisedExpiration = VoucherRenewalEligibility.PromisedExpirationForReplace(
                today, line.Source.CustomerExpirationDate, line.Term);

            // This one query stays per line, and it is worth saying why. Every line takes a
            // DIFFERENT voucher out of the same pool, and the promise each line has to meet is its
            // own (it depends on the term and on the voucher the customer brings back). So the
            // candidates of one line are not the candidates of the next: a batch would have to
            // fetch the whole pool of the fuel to filter it in memory, and a line that finds
            // nothing here would have found something in the pool the batch threw away.
            //
            // The lines that share a fuel AND a promise could share one query, but the order in
            // which the lines consume the pool is the order of the basket, and a group that
            // fetched its candidates up front would take them in a different order than the
            // basket does. That is a different voucher for a customer, so it is not a trade worth
            // making.
            var candidate = await _context.FuelVouchers
                .AsNoTracking()
                .Where(v => v.Status == VoucherStatus.Available
                         && v.Brand == line.Source.Brand
                         && v.FuelTypeId == line.Source.FuelTypeId
                         && v.Liters == line.Source.Liters
                         && v.SupplierExpirationDate >= promisedExpiration
                         && !reservedStock.Contains(v.Id))
                .OrderBy(v => v.SupplierExpirationDate)
                .Select(v => new { v.Id, v.CostPerLiter })
                .FirstOrDefaultAsync(cancellationToken);

            // FirstOrDefaultAsync over an anonymous projection yields null when nothing matches — not a
            // default-initialised struct — so this has to be a null check.
            if (candidate is null)
            {
                var fuelName = await ResolveFuelNameAsync(line.Source, cancellationToken);
                await _notifications.VoucherStockLowAsync(line.Source.Brand, fuelName, 0, 1, cancellationToken);
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
                // The blend of the priced pool of this fuel, for the whole batch: one grouped
                // query instead of one per line.
                var pool = poolBlends.GetValueOrDefault(
                    (line.Source.Brand, line.Source.FuelTypeId, line.Source.Liters));

                var poolLiters = pool.PoolLiters;
                if (poolLiters <= 0m) continue;   // nothing priced: nothing to judge against

                replacementCost = pool.Cost / poolLiters;
            }

            // line.Source is the customer's own voucher, coming back to us, at what we paid for it.
            if (RenewalMargin.ForReplacement(
                    line.LineAmount, line.Source.Liters, replacementCost, line.Source.CostPerLiter)
                == RenewalMarginVerdict.BelowCost)
            {
                var fuelType = fuelTypesById.GetValueOrDefault(line.Source.FuelTypeId);

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

        // The basket: the batch of vouchers, normalized so that the same batch always keys alike,
        // and digested so that the key fits the column no matter how many vouchers the batch has.
        //
        // The key names the batch and nothing else: no clock, no random suffix, and the "renew:"
        // prefix keeps a renewal basket from ever matching a buy-fuel basket.
        var cartSignature = CheckoutBasket.CartSignature(command.Items
            .Select(i => $"{i.VoucherId:N}:{i.TermCode}"));

        var checkoutKey = CheckoutBasket.Key(CheckoutBasket.Renewal, userId, cartSignature);

        var now = DateTime.UtcNow;

        // The order that already holds this batch, if there is one. A retry or a double tap finds it
        // here and is given the same invoice. The read is an equality on an indexed column; it used
        // to be a prefix match on a key that carried a random suffix, which no b-tree can serve and
        // which two requests could both miss.
        var existingOrder = await CheckoutGuard.FindPendingAsync(_context, checkoutKey, cancellationToken);

        if (existingOrder != null)
        {
            if (CheckoutGuard.IsStale(existingOrder, now))
            {
                // A checkout the customer walked away from. The order keeps everything it was, and
                // gives the batch back, so that the next request for it is a new purchase.
                CheckoutGuard.Release(existingOrder);
                await _context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                // The attempt that holds this batch is the current one: a retry finds its invoice,
                // and a request that arrived while it was still running waits for it. Either way it
                // is the same order and the same invoice, and the acquirer is not asked twice.
                var withInvoice = await CheckoutGuard.WaitForInvoiceAsync(
                    _context, existingOrder.Id, cancellationToken);

                if (withInvoice != null)
                {
                    _logger.LogWarning(
                        "Duplicate renewal checkout; reusing pending order {OrderId} / invoice {InvoiceId}",
                        withInvoice.Id, withInvoice.MonobankInvoiceId);

                    return new RenewalCheckoutResponse
                    {
                        OrderId = withInvoice.Id,
                        MonobankInvoiceId = withInvoice.MonobankInvoiceId,
                        PaymentUrl = withInvoice.MonobankPaymentUrl,
                        TotalUah = withInvoice.Price
                    };
                }

                // The attempt is over and it produced no invoice: the acquirer refused it, or the
                // request that owned it died. The batch is free, so this request makes a new
                // attempt.
                CheckoutGuard.Release(existingOrder);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        // Resolve merchant for this account. Outside the try below on purpose: a QA account routed
        // to an unconfigured sandbox is refused outright rather than degraded into an order with
        // no invoice, and never quietly pointed at the live merchant.
        var merchant = _merchantResolver.Resolve(user.PhoneNumber, user.IsQaAccount);
        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            // Left null on purpose: a replacement inherits the SOURCE voucher's legal entity at
            // fulfilment time, so the order need not (and cannot, for a mixed batch) carry one.
            LegalEntityId = null,
            Price = totalUah,
            Kind = OrderKind.Renewal,
            Status = OrderStatus.PendingPayment,
            // The order's own key: unique per order, and the external reference the acquirer is
            // given when this order is refunded. The random suffix is what keeps it that way - the
            // batch's identity is checkout_key, and two orders may share a batch but may not share
            // this.
            IdempotencyKey = $"{checkoutKey}:{Guid.NewGuid():N}",
            CheckoutKey = checkoutKey,
            MonobankMerchant = merchant,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        foreach (var line in resolved)
        {
            // The line item mirrors the source voucher (brand/fuel/nominal) so the shared refund
            // maths — which groups by Brand/FuelTypeId/Liters and refunds unfulfilled units — works
            // unchanged when a renewal order ends up only partially fulfilled.
            //
            // It also now carries the three things that make the line a renewal rather than a
            // description of one: which voucher is being renewed, for what term, and which branch was
            // chosen. They used to live only on voucher_renewal_items, which meant the line the money
            // is charged against could not say what it was for, and the branch could only be
            // reconstructed from a table that has nothing to do with the line.
            order.LineItems.Add(new OrderLineItem
            {
                Id = Guid.CreateVersion7(),
                OrderId = order.Id,
                Brand = line.Source.Brand,
                // The brand's key beside its name - the source voucher's own key, because the line
                // is about the same fuel the customer brings back.
                BrandId = line.Source.BrandId,
                FuelTypeId = line.Source.FuelTypeId,
                Liters = line.Source.Liters,
                Quantity = 1,
                UnitPrice = line.LineAmount,
                LineTotal = line.LineAmount,
                // The term is the input to CanExtend and to the expiry the customer is promised;
                // ck_order_line_renewal_has_term refuses this line without it.
                TermCode = line.Term.Code(),
                SourceVoucherId = line.Source.Id,
                RenewalBranch = line.Branch == VoucherRenewalBranch.Extend
                    ? VoucherRenewalBranchCodes.Extend
                    : VoucherRenewalBranchCodes.Replace
                // OriginalLineTotal left null: a renewal fee has no pump-price reference, so it
                // contributes no "saving vs pump" to the customer savings report.
            });
        }

        _context.Orders.Add(order);

        // No second row to write. The order line above IS the renewal record: it already carries the
        // source voucher, the term and the branch, and its UnitPrice/LineTotal is the money the customer
        // paid. voucher_renewal_items repeated all of it, which is why D-13 could drop it.

        // The order is written BEFORE the acquirer is asked for an invoice, and that order is what
        // decides the race. The second request for this batch is refused by the unique index at this
        // line and never reaches the acquirer - so a double tap cannot become two invoices.
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CheckoutGuard.IsUniqueViolation(ex))
        {
            // Another request for this batch got there first. Its order is the answer to this one -
            // and this request has not asked the acquirer for anything, so there is no second
            // invoice to cancel.
            _context.ChangeTracker.Clear();

            var winner = await CheckoutGuard.FindPendingAsync(_context, checkoutKey, cancellationToken);
            if (winner == null) throw;

            _logger.LogWarning(
                "Concurrent renewal checkout for basket {CheckoutKey} lost the race; reusing order {OrderId}",
                checkoutKey, winner.Id);

            var withInvoice = await CheckoutGuard.WaitForInvoiceAsync(_context, winner.Id, cancellationToken) ?? winner;

            return new RenewalCheckoutResponse
            {
                OrderId = withInvoice.Id,
                MonobankInvoiceId = withInvoice.MonobankInvoiceId,
                PaymentUrl = withInvoice.MonobankPaymentUrl,
                TotalUah = withInvoice.Price
            };
        }

        MonobankInvoiceResponse invoiceResponse;
        try
        {
            var monobankClient = _monobankClientFactory.ForMerchant(merchant);

            invoiceResponse = await monobankClient.CreateInvoiceAsync(
                new MonobankInvoiceRequest
                {
                    Amount = Money.ForMonobank(totalUah),
                    MerchantPaymentInfo = "FuelFlow Voucher Renewal",
                    RedirectUrl = _monobankOptions.RedirectUrl,
                    WebhookUrl = _monobankOptions.WebhookUrl
                }, cancellationToken);

            _logger.LogInformation(
                "Monobank invoice created on {Merchant} merchant for renewal: {InvoiceId}",
                merchant,
                invoiceResponse.InvoiceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Monobank invoice for renewal batch");

            // The invoice never came, so this attempt is over - and an attempt that is over must
            // not hold the batch against the customer's next try. The order itself stays: it is the
            // record of what was attempted.
            CheckoutGuard.Release(order);
            await _context.SaveChangesAsync(cancellationToken);

            throw;
        }

        order.MonobankInvoiceId = invoiceResponse.InvoiceId;
        order.MonobankPaymentUrl = invoiceResponse.PageUrl;

        // The invoice, written onto the order that is already there. A second attempt for this batch
        // is refused by the index above and never reaches this point, so this is the one and only
        // invoice for this batch.
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

        var label = string.IsNullOrEmpty(name) ? source.FuelTypeId.ToString() : name;
        return $"{label} ({source.Brand.ToUpperInvariant()}, {source.Liters:0.##} L)";
    }

    private sealed record ResolvedLine(FuelVoucher Source, VoucherRenewalTerm Term, VoucherRenewalBranch Branch, decimal LineAmount);
}

