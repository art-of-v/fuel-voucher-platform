namespace FuelFlow.Features.Orders.CreateCheckout;

using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.Terms;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;

/// <summary>
/// Prices a bulk cart line by line, refusing the whole of it when any line cannot be sold.
/// </summary>
/// <remarks>
/// Kept apart from the checkout that calls it because it is the part of the bulk path that is not
/// about the order at all: it reads the stock, the manager's term ladder and the cost guards, and
/// answers a price per line plus the cart's total. The handler is left with the basket, the race,
/// the settlement route and the acquirer.
/// </remarks>
public static class BulkCheckoutPricer
{
    /// <summary>
    /// The cart's facts, resolved once by the handler: the names its brands travel under, the fuel
    /// rows, and the stock answers for the whole cart.
    /// </summary>
    public sealed record BulkPricingInput(
        IReadOnlyList<CheckoutItem> Items,
        IReadOnlyDictionary<Guid, string> BrandSlugs,
        IReadOnlyList<FuelTypeEntity> FuelTypes,
        IReadOnlyDictionary<StockKey, SellablePackage> Packages,
        IReadOnlySet<StockKey> InStock,
        IReadOnlyDictionary<StockKey, DateOnly?> BestStockExpirations,
        VoucherTermConfig TermConfig,
        Guid UserId);

    /// <summary>
    /// One priced line. The resolved package rides along so the order line can record what priced it:
    /// the line stores the resulting price, and <c>pricing_basis</c> stores the inputs behind it, which
    /// is the only thing that still answers "why was this 89.06?" once the contract and the fuel's
    /// pricing have moved on.
    /// </summary>
    public sealed record PricedLine(
        CheckoutItem Item,
        SellablePackage Package,
        decimal DiscountPerLiter,
        decimal UnitPrice,
        decimal LineTotal,
        decimal OriginalLineTotal,
        string? TermCode);

    public sealed record BulkPricing(IReadOnlyList<PricedLine> Lines, decimal TotalPrice);

    public static async Task<BulkPricing> PriceAsync(
        BulkPricingInput cart,
        NotificationDispatcher notifications,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var lines = new List<PricedLine>(cart.Items.Count);
        var totalPrice = 0m;

        foreach (var item in cart.Items)
        {
            var fuelType = cart.FuelTypes.FirstOrDefault(f =>
                f.Id == item.FuelTypeId && f.BrandId == item.BrandId);

            if (fuelType == null)
                throw new ArgumentException(
                    $"Invalid fuel type ID: {item.FuelTypeId} for Brand {item.BrandId}");

            var stockKey = StockKey.For(
                cart.BrandSlugs[item.BrandId!.Value], item.FuelTypeId, item.Liters);
            var package = cart.Packages.GetValueOrDefault(stockKey);

            if (package == null)
                throw new ArgumentException(
                    $"No pricing found for fuel type {item.FuelTypeId} at Brand {item.BrandId} for {item.Liters}L");

            // Checked before the pricing guards below, and deliberately so: an empty shelf is the more
            // fundamental answer, and refusing there means the below-cost alert is not fired for a sale
            // that could never have completed anyway. One blocked line fails the whole bulk order, matching
            // the below-cost behaviour — a customer who cannot buy one line cannot check out with it.
            if (!cart.InStock.Contains(stockKey))
                throw new PackageOutOfStockException(
                    cart.BrandSlugs[item.BrandId!.Value], item.FuelTypeId, item.Liters);

            var (discountPerLiter, termCode) =
                CheckoutTermResolver.Resolve(item, cart.TermConfig, cart.UserId, logger);

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
                await notifications.BelowCostAsync(
                    cart.BrandSlugs[item.BrandId!.Value], fuelType.Name,
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
                var bestStockExpiration = cart.BestStockExpirations.GetValueOrDefault(stockKey);

                if (!TermStockCoverage.CanHonour(
                        bestStockExpiration, DateOnly.FromDateTime(SystemClock.UtcNow), purchasedTerm))
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
                throw new ArgumentException("Order total is too large", nameof(cart));
            }

            if (item.Price != lineTotal)
            {
                logger.LogWarning(
                    "Client price {ClientPrice} does not match server price {ServerPrice} for user {UserId}, fuel {FuelTypeId}; using server price",
                    item.Price, lineTotal, cart.UserId, item.FuelTypeId);
            }

            lines.Add(new PricedLine(
                item, package, discountPerLiter, unitPrice, lineTotal,
                ServerPricing.OriginalLineTotal(package, item.Liters, unitPrice, item.Quantity), termCode));
        }

        return new BulkPricing(lines, totalPrice);
    }
}
