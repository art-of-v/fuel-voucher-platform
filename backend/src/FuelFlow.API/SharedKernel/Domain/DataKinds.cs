namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// The two kinds of row the catalogue and stock tables can hold, and the single place that decides
/// which is which.
/// </summary>
/// <remarks>
/// <para>
/// This replaces a plain <c>is_test_data boolean</c> on money rows. The boolean read as a small
/// change and behaved as a large one, because "are these real?" is a property of a row's
/// <em>provenance</em>, not of the transaction — and a boolean is one <c>false</c> away from being a
/// real order with nothing in the database able to notice.
/// </para>
/// <para>
/// Production absorbed two rehearsal rows published as live, both served to real customers by the
/// same anonymous endpoint the marketing site reads: <c>QA-CHECK-95</c> at 1 UAH, and
/// <c>А95 ЄВРО (ТЕСТ 1ГРН)</c> at 1 UAH whose unflagged voucher behind it is what made
/// <c>isAvailable</c> true. The seed script then mirrored the second into a proper QA package,
/// multiplying the mistake. None of it was stopped by a constraint, because there was none.
/// </para>
/// <para>
/// A named value fixes the two parts of that. <c>NOT NULL</c> means "not stated" stops being
/// expressible — the write path is already required to state it
/// (<c>isTestData</c> became <c>bool?</c>), and this makes the column agree rather than relying on
/// every caller. The CHECK pins the set, so a typo cannot land. And a psql session now reads
/// <c>data_kind = 'qa'</c> rather than reasoning about what a bare boolean defaults to.
/// </para>
/// <para>
/// <b>Deliberately not a separate schema.</b> That is the stronger design — a rehearsal row cannot
/// appear in a real result set even with every filter forgotten — but it makes the API and the admin
/// panel schema-aware and the seed scripts target-dependent, which is a larger change than the
/// problem currently justifies. Recorded in issue #321 as the destination if this ever recurs.
/// </para>
/// </remarks>
public static class DataKinds
{
    /// <summary>Real stock, sellable to a real customer, included in settlement.</summary>
    public const string Real = "real";

    /// <summary>
    /// Rehearsal data: emulated, never sold to a real customer, never fulfilled from real stock.
    /// </summary>
    public const string Qa = "qa";

    /// <summary>The complete set, for the CHECK constraint. Keep in step with the migration.</summary>
    public static readonly string[] All = [Real, Qa];

    /// <summary>
    /// The pool a QA caller reads. The inverse of "is this caller QA" everywhere it is used, so it
    /// is expressed once here rather than at each call site.
    /// </summary>
    public static string ForPool(bool isTestAccount) => isTestAccount ? Qa : Real;

    public static bool IsKnown(string? value) => value is Real or Qa;
}