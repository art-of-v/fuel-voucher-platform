namespace FuelFlow.Features.Vouchers.Exchange;

/// <summary>
/// Pure, deterministic pairing of retired OLD stock vouchers to freshly-imported NEW ones for an
/// operator→provider exchange (planning #104). Pairing is flexible: the operator may select a
/// different number of old vouchers than the provider PDF contains. We pair 1:1 only WITHIN the same
/// <c>(provider, fuel, liters)</c> bucket, in a stable sorted order; leftover old vouchers expire
/// unpaired (null NewVoucherId) and leftover new vouchers simply enter stock with no row.
///
/// Kept free of EF/DB types so it is unit-testable in isolation.
/// </summary>
public static class VoucherExchangePairing
{
    /// <summary>A voucher reduced to the fields pairing needs.</summary>
    public sealed record Candidate(Guid Id, string Provider, string FuelTypeId, decimal Liters, string VoucherNumber);

    public sealed record Result(
        IReadOnlyList<(Guid OldId, Guid? NewId)> Pairs,
        int PairedCount,
        int UnpairedOldCount,
        int UnpairedNewCount);

    /// <summary>
    /// Pairs <paramref name="olds"/> to <paramref name="news"/>. The returned <see cref="Result.Pairs"/>
    /// has exactly one entry per OLD voucher (NewId set when paired, null when not), so the caller can
    /// insert one exchange row per old voucher.
    /// </summary>
    public static Result Pair(IReadOnlyList<Candidate> olds, IReadOnlyList<Candidate> news)
    {
        // Bucket new vouchers by (provider, fuel, liters); within a bucket, hand them out in a stable
        // order (by voucher number) so the pairing is deterministic.
        var buckets = news
            .GroupBy(Key)
            .ToDictionary(
                g => g.Key,
                g => new Queue<Guid>(g.OrderBy(n => n.VoucherNumber, StringComparer.Ordinal).Select(n => n.Id)));

        var pairs = new List<(Guid OldId, Guid? NewId)>(olds.Count);
        var paired = 0;

        foreach (var old in olds.OrderBy(o => o.VoucherNumber, StringComparer.Ordinal))
        {
            if (buckets.TryGetValue(Key(old), out var queue) && queue.Count > 0)
            {
                pairs.Add((old.Id, queue.Dequeue()));
                paired++;
            }
            else
            {
                pairs.Add((old.Id, null));
            }
        }

        var unpairedNew = buckets.Values.Sum(q => q.Count);
        var unpairedOld = olds.Count - paired;

        return new Result(pairs, paired, unpairedOld, unpairedNew);
    }

    private static (string, string, decimal) Key(Candidate c)
        => (c.Provider.ToLowerInvariant(), c.FuelTypeId, c.Liters);
}
