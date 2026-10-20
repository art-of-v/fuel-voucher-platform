namespace FuelFlow.Features.Orders.GetAdminPurchases;

public sealed record GetAdminPurchasesQuery(
    /// <summary>
    /// 1-based page number. Clamped by <see cref="PageLimits.ClampPage"/>.
    /// </summary>
    int Page = 1,
    /// <summary>
    /// Rows per page. Clamped by <see cref="PageLimits.ClampPageSize"/>, so a caller cannot
    /// ask for the whole table by asking for a large page.
    /// </summary>
    int Limit = 50);
