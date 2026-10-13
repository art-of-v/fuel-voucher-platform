namespace FuelFlow.API.Features.Orders.SharedServices.Monobank;

/// <summary>
/// How much statement one request may ask for, and how to ask for more than that without losing any
/// of it.
/// </summary>
/// <remarks>
/// Exists because the acquirer's limit is lower than the window we want to re-read, and the two
/// facts live on opposite sides of the HTTP call: the job knows how far back to look, the acquirer
/// alone knows how far back it will answer. Keeping the shape of the slicing next to the number means
/// neither side has to be re-derived from the other.
/// </remarks>
public static class MonobankStatementWindow
{
    /// <summary>
    /// The widest span a single statement request may cover.
    /// </summary>
    /// <remarks>
    /// A wider range is answered with
    /// <c>400 {"errCode":"1001","errText":"to-'from' cannot be more than 31 days"}</c> and nothing
    /// else - the whole request, not the excess. So this is a hard shape of the request rather than a
    /// throttle to back off from, and a window wider than it has to be split rather than shortened:
    /// shortening silently drops the oldest days of every pass instead of fetching them a slice at a
    /// time.
    /// </remarks>
    public const int MaxRangeDays = 31;

    /// <summary>
    /// <paramref name="fromUtc"/> to <paramref name="toUtc"/> cut into consecutive slices, none wider
    /// than <see cref="MaxRangeDays"/>.
    /// </summary>
    /// <remarks>
    /// Oldest slice first, and each one half-open exactly like the client's own bounds, so slice N's
    /// upper bound is slice N+1's lower bound. The tiling is the point: an overlap would be absorbed
    /// by the upsert, but a gap would drop that period from every pass, and nothing downstream would
    /// report the loss - the act would simply be short.
    /// </remarks>
    /// <returns>The slices, or an empty list when the range covers no period at all.</returns>
    public static IReadOnlyList<StatementSlice> Split(DateTime fromUtc, DateTime toUtc)
    {
        var slices = new List<StatementSlice>();

        var from = fromUtc;
        while (from < toUtc)
        {
            var to = from.AddDays(MaxRangeDays);
            if (to > toUtc)
            {
                to = toUtc;
            }

            slices.Add(new StatementSlice(from, to));
            from = to;
        }

        return slices;
    }
}

/// <param name="FromUtc">Inclusive start of the slice.</param>
/// <param name="ToUtc">Exclusive end of the slice.</param>
public readonly record struct StatementSlice(DateTime FromUtc, DateTime ToUtc);