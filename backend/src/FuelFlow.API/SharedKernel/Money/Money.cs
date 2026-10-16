namespace FuelFlow.SharedKernel;

/// <summary>
/// Currency unit conversions, in one place so unit mistakes stay greppable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two currencies, not two representations of one.</b> The domain stores money in UAH as
/// <c>numeric(12,2)</c> (orders, line items, the fuel catalogue, bonus balances). The acquiring side
/// speaks kopecks because that is what Monobank's API takes and returns, so <c>refunds</c> and
/// <c>monobank_statement_entries</c> hold kopecks in <c>numeric(14,0)</c>. Both are decimal - an integer
/// type holding money is the thing that goes wrong at scale and invites unit arithmetic - and the unit
/// lives in the column name on both sides.
/// </para>
/// <para>
/// <b>Kopecks are not "the same money in another type".</b> A refund is a kopeck amount because the
/// refund call is made in kopecks; it is not a UAH amount scaled by 100 for convenience. So
/// <see cref="ToKopecks(decimal)"/> rounds to a whole kopeck rather than multiplying a possibly-fractional
/// figure, and the half-away-from-zero rule is stated in one line instead of at each of the nine call
/// sites that used to repeat <c>(long)Math.Round(…)</c>.
/// </para>
/// <para>
/// <see cref="ForMonobank"/> is the single place a kopeck value becomes a <see cref="long"/> for the wire.
/// It is the only reason a <c>long</c> still exists in this codebase's money path, and it is named so that
/// a reviewer looking at a cast has to walk over here to see why it is allowed.
/// </para>
/// </remarks>
public static class Money
{
    /// <summary>UAH → kopecks, rounded to a whole kopeck, half away from zero.</summary>
    /// <remarks>
    /// Rounding belongs here, not at the call site: 456.705 UAH is 45670.5 kopecks, and a value that
    /// is only ever an integer kopeck after this call is what makes the stored <c>numeric(14,0)</c>
    /// safe. Half away from zero matches the arithmetic already applied at checkout, so a domain amount
    /// and its stored kopeck image cannot disagree by a rounding step.
    /// </remarks>
    public static decimal ToKopecks(decimal uah)
        => Math.Round(uah * 100m, MidpointRounding.AwayFromZero);

    /// <summary>Kopecks → UAH. Exact: dividing a whole kopeck by 100 cannot lose anything at 2 dp.</summary>
    public static decimal FromKopecks(decimal kopecks) => kopecks / 100m;

    /// <summary>
    /// The one cast to <see cref="long"/> that money is allowed to make, for the Monobank request DTOs
    /// whose contract is JSON integers.
    /// </summary>
    /// <remarks>
    /// Deliberately total rather than partial: a name like <c>ToKopecksLong</c> invited being used
    /// anywhere a <c>long</c> was wanted, and it was used nine times. Naming it for the destination
    /// rather than the type means a new call site has to say <em>why</em> it is crossing the API line,
    /// and an internal kopeck calculation has nothing to call.
    /// </remarks>
    public static long ForMonobank(decimal uah)
        => decimal.ToInt64(ToKopecks(uah));

    /// <summary>
    /// Kopecks → the <see cref="long"/> the Monobank wire wants. <b>Input is kopecks, not UAH.</b>
    /// </summary>
    /// <remarks>
    /// This exists because <see cref="ForMonobank"/> takes UAH and the two are easy to confuse: routing
    /// an already-kopeck amount through <see cref="ForMonobank"/> multiplies a refund by 100. Two
    /// names rather than one cast at the call site, because that mistake costs money and a comment
    /// does not stop it.
    /// <para>
    /// Truncates rather than rounds, and that is only safe because kopeck values are whole by
    /// construction - <see cref="ToKopecks"/> rounds - so there is never a fraction to lose.
    /// </para>
    /// </remarks>
    public static long KopecksForWire(decimal kopecks)
        => decimal.ToInt64(kopecks);
}
