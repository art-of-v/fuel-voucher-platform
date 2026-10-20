namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Builds the id a sellable package is addressed by, from the three things that identify one.
/// </summary>
/// <remarks>
/// A package used to be a row and carried its own id, which is why the id looked like a
/// <c>package</c> to everything downstream. It is not stored anywhere - no column, no foreign key -
/// so it can be derived instead: the fuel, the pool it is sold in, and the size. That triple is
/// exactly what <c>(fuel_type_id, data_kind, liters)</c> is unique on.
/// <para>
/// It was keyed on the offer until 2026-10-19. The offer id was a string built from the fuel and the
/// pool, so the identity did not change - what changed is that nothing has to keep the offer around
/// to derive it.
/// </para>
/// <para>
/// One place formats it. The shelf, availability and checkout must all produce the same string for
/// the same package, and three string interpolations are three chances to disagree.
/// </para>
/// </remarks>
public static class PackageIds
{
    /// <summary>The separator between the parts. Not valid in any of them.</summary>
    private const char Separator = ':';

    /// <summary>
    /// The id of the package that is <paramref name="liters"/> of <paramref name="fuelTypeId"/>, sold
    /// in <paramref name="dataKind"/>.
    /// </summary>
    public static string For(Guid fuelTypeId, string dataKind, decimal liters)
        => $"{fuelTypeId}{Separator}{dataKind}{Separator}{FormatLiters(liters)}";

    /// <summary>
    /// Litres as they are written in an id: a whole number stays whole, because
    /// <c>10</c> and <c>10.0</c> are the same nominal and must not become two package ids.
    /// </summary>
    private static string FormatLiters(decimal liters)
        => liters == decimal.Truncate(liters)
            ? ((long)liters).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : liters.ToString(System.Globalization.CultureInfo.InvariantCulture);
}