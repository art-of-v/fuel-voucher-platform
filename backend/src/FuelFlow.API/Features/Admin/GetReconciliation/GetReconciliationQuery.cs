namespace FuelFlow.Features.Admin.GetReconciliation;

/// <summary>
/// The admin's reconciliation screen, and the page of the three-way match it shows.
/// </summary>
/// <remarks>
/// <para>
/// The three-way match is a row per order - the order, what it should have received, what it
/// did receive, and what was refunded - so it grows with every order the shop has ever taken.
/// It used to be answered with all of them, together with each order's fulfillments, its
/// vouchers and its line items, and the server held the lot in memory to draw a table that
/// shows fifty rows.
/// </para>
/// <para>
/// Everything else on this screen is an aggregate and stays an aggregate: the counts, the
/// funnel, the money ledger and the revenue summary are computed by the database over the
/// whole table, because a summary that described the page would be a different number on
/// every page.
/// </para>
/// </remarks>
public sealed record GetReconciliationQuery(
    /// <summary>
    /// 1-based page number. Clamped by <see cref="PageLimits.ClampPage"/>.
    /// </summary>
    int Page = 1,
    /// <summary>
    /// Rows per page. Clamped by <see cref="PageLimits.ClampPageSize"/>, so a caller cannot
    /// ask for the whole table by asking for a large page.
    /// </summary>
    int Limit = 50);
