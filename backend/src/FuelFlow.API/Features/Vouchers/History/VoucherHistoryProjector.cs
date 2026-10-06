namespace FuelFlow.Features.Vouchers.History;

/// <summary>
/// Assembles a voucher's customer-facing timeline (purchase + each renewal) from the renewal-item
/// records, walking the chain back across <em>replace</em> swaps so a single tank of fuel reads as
/// one continuous asset even though a replace issued a physically different voucher id.
///
/// Pure and side-effect free: the handler loads the rows, this turns them into ordered events.
/// </summary>
public static class VoucherHistoryProjector
{
    /// <summary>A renewal line, flattened to just what the timeline needs.</summary>
    public readonly record struct RenewalNode(
        Guid SourceVoucherId,
        Guid? FulfilledVoucherId,
        DateTime Date,
        string? TermCode,
        decimal? Amount,
        DateOnly? PreviousCustomerExpiration,
        DateOnly? NewCustomerExpiration);

    /// <summary>The original purchase of the oldest voucher in the chain.</summary>
    public readonly record struct PurchaseNode(DateTime Date, decimal Amount);

    /// <summary>
    /// Walks the renewal chain back across replace swaps to the oldest voucher in the lineage. For an
    /// un-renewed or extend-only voucher this is the voucher itself; for a replaced one it is the
    /// original voucher the customer first bought. Used to file a replacement under the customer's
    /// ORIGINAL purchase order in the wallet, so one tank of fuel stays one asset.
    /// </summary>
    public static Guid ResolveRootVoucherId(
        Guid currentVoucherId,
        IReadOnlyCollection<RenewalNode> renewals)
    {
        var cur = currentVoucherId;
        var guard = new HashSet<Guid> { cur };
        while (true)
        {
            var producedByReplace = renewals.FirstOrDefault(r =>
                r.FulfilledVoucherId == cur && r.SourceVoucherId != cur);
            if (producedByReplace.FulfilledVoucherId is null) break;
            if (!guard.Add(producedByReplace.SourceVoucherId)) break;
            cur = producedByReplace.SourceVoucherId;
        }
        return cur;
    }
    /// <summary>
    /// Builds the ordered (oldest-first) history for the voucher the customer currently holds.
    /// </summary>
    /// <param name="currentVoucherId">The voucher in the customer's wallet right now.</param>
    /// <param name="nominalLiters">The voucher's litres - constant across its life, shown on every row.</param>
    /// <param name="renewals">Every renewal line that touched any voucher in this lineage.</param>
    /// <param name="purchaseLookup">
    /// Resolves the purchase of a given (root) voucher id, or null when the root was not a purchase
    /// (e.g. operator-issued stock with no owning order).
    /// </param>
    public static List<VoucherHistoryEventDto> Build(
        Guid currentVoucherId,
        decimal nominalLiters,
        IReadOnlyCollection<RenewalNode> renewals,
        Func<Guid, PurchaseNode?> purchaseLookup)
    {
        // 1. Walk back across replace swaps to find the whole voucher lineage (oldest -> current).
        //    A replace that produced `cur` is a renewal with FulfilledVoucherId == cur and a DIFFERENT
        //    source; follow its source back. Extends keep the same id, so they do not move the chain.
        var chain = new List<Guid> { currentVoucherId };
        var cur = currentVoucherId;
        var guard = new HashSet<Guid> { cur };
        while (true)
        {
            var producedByReplace = renewals.FirstOrDefault(r =>
                r.FulfilledVoucherId == cur && r.SourceVoucherId != cur);
            if (producedByReplace.FulfilledVoucherId is null) break;      // default struct -> no match
            if (!guard.Add(producedByReplace.SourceVoucherId)) break;      // defensive: cycle
            cur = producedByReplace.SourceVoucherId;
            chain.Insert(0, cur);
        }

        var lineage = chain.ToHashSet();
        var events = new List<VoucherHistoryEventDto>();

        // 2. Purchase of the oldest voucher in the lineage.
        if (purchaseLookup(chain[0]) is { } purchase)
        {
            events.Add(new VoucherHistoryEventDto(
                VoucherHistoryEventType.Purchase,
                purchase.Date,
                nominalLiters,
                purchase.Amount,
                ValidFrom: null,
                ValidTo: null,
                TermCode: null));
        }

        // 3. Every renewal that acted on a voucher in the lineage (extend or replace).
        foreach (var r in renewals.Where(r => lineage.Contains(r.SourceVoucherId)))
        {
            events.Add(new VoucherHistoryEventDto(
                VoucherHistoryEventType.Renewal,
                r.Date,
                nominalLiters,
                r.Amount,
                r.PreviousCustomerExpiration,
                r.NewCustomerExpiration,
                r.TermCode));
        }

        return events.OrderBy(e => e.Date).ToList();
    }
}
