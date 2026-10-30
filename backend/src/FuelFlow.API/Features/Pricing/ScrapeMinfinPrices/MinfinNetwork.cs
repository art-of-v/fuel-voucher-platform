using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Pricing.ScrapeMinfinPrices;

/// <summary>
/// One network's row in Minfin's table, and what its five columns mean for our catalogue of that
/// network.
/// </summary>
/// <remarks>
/// <para>
/// <b>KLO needs no table of correspondences.</b> Its catalogue is named after Minfin's columns -
/// "А 95+", "А 95", "А 92", "ДП", "Газ" - because an operator reading the table types the column's
/// name, so the grade's own key is the match.
/// </para>
/// <para>
/// <b>WOG needs one.</b> WOG names a grade in its own words - "95 Mustang Євро5-Е10", "ДП Євро5" -
/// and Minfin's column says "А 95+" and "ДП". The two vocabularies do not fold onto each other, so
/// the correspondence is written out below rather than guessed at. It was read off the catalogue and
/// the table on 2026-10-30, when the two agreed on every figure: the column's price for "А 95+" was
/// the price of "95 Mustang Євро5-Е10", and so on.
/// </para>
/// <para>
/// <b>What is not in the table is not priced.</b> WOG sells two grades Minfin does not average -
/// "100 Mustang Євро5-Е0", "ДП Mustang+" and "AdBlue" - and a column that does not exist cannot
/// move them. They keep the last price they had, which is a fact this importer reports rather than
/// hides.
/// </para>
/// </remarks>
public sealed class MinfinNetwork
{
    /// <summary>
    /// WOG's grades, as Minfin's column names them and as our catalogue names them.
    /// </summary>
    private static readonly (string MinfinGrade, string OurFuelName)[] WogGrades =
    [
        ("А 95+", "95 Mustang Євро5-Е10"),
        ("А 95", "95 Євро5-Е10"),
        ("ДП", "ДП Євро5"),
        ("Газ", "ГАЗ"),
    ];

    private readonly IReadOnlyDictionary<string, string>? _grades;

    private MinfinNetwork(string slug, (string MinfinGrade, string OurFuelName)[]? grades)
    {
        Slug = slug;
        _grades = grades?.ToDictionary(g => Key(g.MinfinGrade), g => Key(g.OurFuelName), StringComparer.Ordinal);
    }

    /// <summary>KLO, whose catalogue is named after the columns themselves.</summary>
    public static readonly MinfinNetwork Klo = new("klo", null);

    /// <summary>WOG, which names its grades in its own words.</summary>
    public static readonly MinfinNetwork Wog = new("wog", WogGrades);

    /// <summary>The networks Minfin prices, and the only ones this table is read for.</summary>
    public static readonly MinfinNetwork[] All = [Klo, Wog];

    /// <summary>The brand's slug - how both Minfin and our catalogue spell the network.</summary>
    public string Slug { get; }

    /// <summary>
    /// Whether a Minfin column IS the name our catalogue uses for that grade.
    /// </summary>
    /// <remarks>
    /// It decides two things that only follow from an unnamed vocabulary: a grade the catalogue
    /// lacks can be created from the column (that is how a KLO fuel appears with sizes and a price),
    /// and a fuel with no code can be given the column's name as one. For WOG neither is true - the
    /// column is not the brand's word for the grade, so writing it into the catalogue would rename
    /// the shelf.
    /// </remarks>
    public bool OwnsTheColumnNames => _grades is null;

    /// <summary>The network the slug names, or null when Minfin's table is not read for it.</summary>
    public static MinfinNetwork? For(string slug)
    {
        var wanted = slug.Trim();
        return All.FirstOrDefault(n => n.Slug.Equals(wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The key of the catalogue fuel a Minfin grade prices, or null when the grade has no
    /// counterpart in that network's catalogue.
    /// </summary>
    public string? OurGradeKey(string minfinGrade)
    {
        var key = Key(minfinGrade);
        if (_grades is null)
        {
            return key;
        }

        return _grades.TryGetValue(key, out var ours) ? ours : null;
    }

    /// <summary>
    /// The key a grade is compared by: <see cref="FuelGrade.Key(string)"/>, which folds "А95",
    /// "А 95" and "A-95" onto one spelling - and a name that says no grade at all is compared as
    /// itself, because two rows with no grade are still two rows.
    /// </summary>
    public static string Key(string name)
        => FuelGrade.Key(name) ?? name.Trim().ToUpperInvariant();
}
