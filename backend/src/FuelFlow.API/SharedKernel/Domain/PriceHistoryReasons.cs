namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// The doors a price change can come through, and the complete set for the CHECK constraint. Keep in
/// step with the migration, as <see cref="DataKinds.All"/> asks of its own list.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the column exists.</b> <c>provider_event_outbox</c> already records that a price changed,
/// with the before and after values as jsonb and a human-readable summary. That answers "what
/// happened, and who did it"; it cannot answer "was this the pump import or a person typing a
/// number", because the event type is the fuel, not the cause. A price row is read far more often
/// than an audit event, and the first question about a surprising price is always which of the
/// importers or the operator screen produced it.
/// </para>
/// <para>
/// The names are the write paths, one per caller of <c>PriceHistoryRecorder</c>. Adding a path means
/// adding a name here and widening the CHECK in the same change - a caller cannot pass a reason the
/// database has not heard of, so the list cannot silently drift from the paths that exist.
/// </para>
/// </remarks>
public static class PriceHistoryReasons
{
    /// <summary>An operator typed the price in the admin panel, or an admin API called it directly.</summary>
    public const string Operator = "operator";

    /// <summary>The OKKO pump-price import moved the column price on its own, unattended.</summary>
    public const string PumpImport = "pump-import";

    /// <summary>
    /// The catalogue import created the offer, so there is no previous price: the row records what
    /// the offer was born with, which for this path is a pump price with the cost still unknown.
    /// </summary>
    public const string CatalogueImport = "catalogue-import";

    /// <summary>
    /// An operator entered a batch cost, and the blended cost across imports became the offer's
    /// supplier price. See <c>BlendedCostRecalculator</c>.
    /// </summary>
    public const string BatchCost = "batch-cost";

    /// <summary>
    /// A voucher exchange expired old stock and released its cost back into the pool, which moved the
    /// blended cost and therefore the price. See <c>ConfirmVoucherExchangeCommandHandler</c>.
    /// </summary>
    public const string ExchangeRelease = "exchange-release";

    /// <summary>The row that opened the history, written by the migration that created this table.</summary>
    public const string Backfill = "backfill";

    /// <summary>The complete set, for the CHECK constraint. Keep in step with the migration.</summary>
    public static readonly string[] All =
        [Operator, PumpImport, CatalogueImport, BatchCost, ExchangeRelease, Backfill];
}