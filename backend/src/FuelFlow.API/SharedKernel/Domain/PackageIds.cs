namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Builds the id a sellable package is addressed by, from the two things that actually identify one.
/// </summary>
/// <remarks>
/// A package used to be a row and carried its own id, which is why the id looked like a
/// <c>package</c> to everything downstream. It is not stored anywhere - no column, no foreign key -
/// so it can be derived instead: the offer names the pool and the fuel, the nominal names the size,
/// and <c>offerId:liters</c> is unique exactly where <c>UNIQUE (offer_id, liters)</c> used to be.
/// <para>
/// One place formats it. The shelf, availability and checkout must all produce the same string for
/// the same package, and three string interpolations are three chances to disagree.
/// </para>
/// </remarks>
public static class PackageIds
{
    /// <summary>The separator between the offer and the size. Not valid in either part.</summary>
    private const char Separator = ':';

    /// <summary>
    /// The id of the package that is <paramref name="liters"/> of whatever
    /// <paramref name="offerId"/> offers.
    /// </summary>
    public static string For(string offerId, decimal liters)
        => $"{offerId}{Separator}{FormatLiters(liters)}";

    /// <summary>
    /// Litres as they are written in an id: a whole number stays whole, because
    /// <c>10</c> and <c>10.0</c> are the same nominal and must not become two package ids.
    /// </summary>
    private static string FormatLiters(decimal liters)
        => liters == decimal.Truncate(liters)
            ? ((long)liters).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : liters.ToString(System.Globalization.CultureInfo.InvariantCulture);
}