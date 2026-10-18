namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Which supply route a voucher was acquired on. Immutable, and a fact about where the fuel came
/// <em>from</em> - not who ended up holding it and not how it was paid for.
/// </summary>
/// <remarks>
/// <para>
/// Those three are separate axes on purpose, and conflating any two of them is what makes per-route
/// reporting impossible later. <see cref="DataKinds"/> answers "is this emulated". The holder
/// (<c>assigned_to_user_id</c>) answers "who has it". The payment answers "how did money arrive".
/// This answers "which supplier relationship did we buy it under", because the cost of a voucher
/// differs by route while nothing else about the voucher does - it is the same fuel, redeemed the
/// same way, sold at the same price.
/// </para>
/// <para>
/// <b>Why a named value rather than a boolean.</b> A boolean here would be one flipped value away
/// from being a voucher whose cost sits in the wrong bucket, with nothing in the database able to
/// notice - the same failure <see cref="DataKinds"/> documents, on a different axis.
/// <c>NOT NULL</c> means "not stated" stops being expressible, and the CHECK pins the set so a typo
/// cannot land. A psql session reads <c>acquisition_source = 'partner'</c> rather than reasoning
/// about what a bare boolean defaults to.
/// </para>
/// <para>
/// <b>Why the values are named for the relationship rather than for the party.</b> A voucher's route
/// is a property of the supply arrangement, and the party behind it is already recorded - on the
/// supplier, and on the legal entity that owns the stock. Naming the value after the counterparty
/// would duplicate that reference and put a second, independently-updatable copy of a supplier fact
/// on a table that holds tens of thousands of rows.
/// </para>
/// </remarks>
public static class AcquisitionSources
{
    /// <summary>
    /// Bought through the primary supply arrangement, with the full acquisition documentation.
    /// </summary>
    public const string Direct = "direct";

    /// <summary>
    /// Bought through a secondary supply arrangement. Same product, different acquisition cost, and
    /// the cost of these vouchers does not belong in the same reporting bucket as
    /// <see cref="Direct"/>.
    /// </summary>
    public const string Partner = "partner";

    /// <summary>The complete set, for the CHECK constraint. Keep in step with the migration.</summary>
    public static readonly string[] All = [Direct, Partner];

    /// <summary>
    /// The route a voucher gets when nothing states one. Every voucher acquired so far came through
    /// <see cref="Direct"/>, so this default is the historical fact rather than a guess - and it is
    /// why the column can be <c>NOT NULL</c> on a table whose write paths do not all mention it.
    /// </summary>
    public const string Default = Direct;

    public static bool IsKnown(string? value) => value is Direct or Partner;
}