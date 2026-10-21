using System.Text;

namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// The grade a fuel's name speaks of, as one key - so that two spellings of one grade are one grade.
/// </summary>
/// <remarks>
/// <para>
/// "А-95" and "A-95" are the same grade and two different strings. The owner's decision
/// (planning #349, 2026-10-21) is that the catalogue says such a grade once, and this is the
/// rule that says it: two names that fold onto the same key are one grade, whatever language
/// and whatever punctuation they are written in.
/// </para>
/// <para>
/// <b>The rule, in order.</b> Fold every Cyrillic letter onto its Latin twin, lower-case what is
/// left, drop everything that is not a letter, a digit or a plus, and spell the four words that
/// have a standard Latin spelling the way the classifier spells them. So "А-95", "A-95" and "а 95"
/// all become "a95"; "ДП" and "DP" both become "dp"; "Пульс 95" and "Pulls 95" both become
/// "pulls95"; "Газ" and "ГАЗ" both become "gas".
/// </para>
/// <para>
/// <b>The plus is kept.</b> "А 95+" and "А 95" are two grades at KLO, and a rule that merged
/// them would refuse a fuel the network really sells. Merging too much is the dangerous
/// direction here: the constraint refuses a row, so a rule that is too eager refuses a fuel.
/// </para>
/// <para>
/// <b>The database applies the same rule.</b> <c>fuelflow_fuel_grade_key(name)</c> is the SQL
/// twin of this class, and <c>fuel_types.grade_key</c> is what it computes; the unique index
/// <c>ux_fuel_types_brand_grade_key</c> refuses two live rows of one brand that share a key.
/// The two have to say the same thing, or the constraint would refuse a fuel the application
/// believes is a different grade - so a test reads the SQL function and this class over every
/// name the catalogue holds and compares them.
/// </para>
/// <para>
/// <b>And the classifier agrees with it.</b> <see cref="FuelFlow.Features.Vouchers.Import.OkkoFuelClassifier"/>
/// reads a name into a brand-specific category, and two names with one key always land in one
/// category: that is the property the agreement test asserts over the same corpus.
/// </para>
/// </remarks>
public static class FuelGrade
{
    /// <summary>
    /// The Cyrillic letters, upper case first and lower case second, and the Latin letters they
    /// fold onto - the same order, the same length.
    /// <para>
    /// The two strings are the rule, and they are written out rather than computed: the SQL
    /// function has to say the same thing in <c>translate()</c>, and a table that can be read
    /// side by side is a table that can be checked.
    /// </para>
    /// </summary>
    private const string Cyrillic =
        "АБВГҐДЕЄЁЖЗИІЇЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ" +
        "абвгґдеєёжзиіїйклмнопрстуфхцчшщъыьэюя";

    private const string Latin =
        "ABVGGDEEEJZIIIJKLMNOPRSTUFHCCSS_Y_EUA" +
        "abvggdeeejziiijklmnoprstufhccss_y_eua";

    /// <summary>
    /// The words whose Latin spelling is not what the letter-by-letter fold produces, spelled the
    /// way the classifier spells them - so that "Пульс 95" and "Pulls 95" are one grade and the
    /// key says what the classifier says.
    /// </summary>
    private static readonly (string From, string To)[] Words =
    [
        ("puls", "pulls"),
        ("dizel", "diesel"),
        ("gaz", "gas"),
        ("evro", "euro")
    ];

    /// <summary>
    /// The key a name speaks of, or null when the name says no grade at all.
    /// </summary>
    /// <param name="name">A fuel's display name, in whatever language it was typed.</param>
    public static string? Key(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        // The punctuation goes before the words are spelled their standard way, and that order is
        // not cosmetic: the soft sign folds onto a placeholder, so "Пульс" is "pul_s" until the
        // placeholder is dropped - and it is "puls" only then, which is the word the spelling of
        // "Pulls" is written against.
        var key = Fold(name);

        foreach (var (from, to) in Words)
            key = key.Replace(from, to, StringComparison.Ordinal);

        return key.Length == 0 ? null : key;
    }

    /// <summary>
    /// Whether two names speak of one grade.
    /// </summary>
    /// <remarks>
    /// A name that says no grade says nothing, so two of them are not one grade: this is
    /// "is this the same fuel", not "are these both empty".
    /// </remarks>
    public static bool SameGrade(string? left, string? right)
    {
        var key = Key(left);
        return key is not null && key == Key(right);
    }

    /// <summary>
    /// The name with the Cyrillic letters folded onto their Latin twins, lower-cased, and
    /// everything that is not a letter, a digit or a plus dropped.
    /// </summary>
    private static string Fold(string name)
    {
        var transliterated = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            var index = Cyrillic.IndexOf(c);
            transliterated.Append(index >= 0 ? Latin[index] : c);
        }

        var folded = transliterated.ToString().ToLowerInvariant();

        var key = new StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            if (c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '+')
                key.Append(c);
        }

        return key.ToString();
    }
}
