namespace FuelFlow.Features.Orders.GetSavingsReport;

/// <summary>
/// What the customer sees about their own fuel purchases. Cost, margin and any operator loss are
/// deliberately absent — this projection carries only the customer's own paid amount, litres and
/// saving vs the pump. "Redeemed at station" is not included in the MVP (depends on a station
/// redemption feed that does not exist yet); only the owned-but-unredeemed remaining balance is shown.
/// </summary>
public sealed record SavingsReportDto
{
    /// <summary>Number of paid (non-cancelled, non-fully-refunded) orders.</summary>
    public int OrdersCount { get; init; }

    /// <summary>Total the customer paid across those orders, whole UAH.</summary>
    public int TotalPaid { get; init; }

    /// <summary>Total litres bought across those orders.</summary>
    public decimal TotalLiters { get; init; }

    /// <summary>
    /// Total saving vs the pump, frozen at purchase (Σ max(0, OriginalLineTotal − LineTotal)).
    /// Lines with no captured pump reference (older orders, renewals) contribute nothing, so this is
    /// only ever a lower bound — never negative, never inflated.
    /// </summary>
    public int TotalSavings { get; init; }

    /// <summary>Vouchers the customer still owns and has not redeemed at a station.</summary>
    public int RemainingVouchers { get; init; }

    /// <summary>Litres still owned and unredeemed.</summary>
    public decimal RemainingLiters { get; init; }

    /// <summary>
    /// Per-month savings breakdown over the requested period, oldest month first. Carries only the
    /// same leak-free figures as the summary (paid / saved / litres) — never cost, margin or loss.
    /// </summary>
    public IReadOnlyList<MonthlySavings> Monthly { get; init; } = [];
}

/// <summary>One calendar month's slice of the savings summary (grouped by order <c>CreatedAtUtc</c>).</summary>
/// <param name="Month">Month key, <c>yyyy-MM</c>.</param>
/// <param name="Paid">Total paid that month, whole UAH.</param>
/// <param name="Saved">Saving vs the pump that month (Σ max(0, OriginalLineTotal − LineTotal)).</param>
/// <param name="Liters">Litres bought that month.</param>
public sealed record MonthlySavings(string Month, int Paid, int Saved, decimal Liters);
