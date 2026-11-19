namespace FuelFlow.Features.Settings.SharedModels;

/// <summary>
/// The colour themes the mobile app can open in — the server-side copy of the catalogue the client
/// draws its picker from.
/// </summary>
/// <remarks>
/// <para>
/// The palettes are the client's: <c>mobile/src/core/design/themes.ts</c> owns the colours and
/// <c>admin/src/lib/themes.ts</c> mirrors the metadata. The server keeps the ids and nothing else,
/// because the only question it has to answer is "is this a theme that exists", and answering it
/// with a colour would be a second copy of the palette that would drift from the one the phone
/// paints.
/// </para>
/// <para>
/// The list is duplicated rather than shared because the three packages are separate builds and
/// nothing imports across them; the guard against drift is <see cref="TryNormalize"/>, which refuses
/// an id it does not know. A theme added to the client and not here fails loudly on the first
/// attempt to set it, instead of being stored and silently read back as the default.
/// </para>
/// </remarks>
public static class AppThemes
{
    /// <summary>
    /// What the app opens in when nobody has ever written the setting. Obsidian is the app's own
    /// default (<c>mobile/src/core/design/tokens.ts</c>) and the ELITE theme in the profile picker,
    /// so a fresh installation and an explicitly configured one look the same.
    /// </summary>
    public const string Default = "obsidian";

    /// <summary>
    /// Every theme id the client knows, in the order its picker shows them.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "lemberg",
        "white",
        "blue",
        "obsidian",
        "nova",
        "glass",
        "sorbet",
        "blade",
    };

    /// <summary>
    /// Whether <paramref name="id"/> is a theme. Case-insensitive, because the value arrives from a
    /// form whose markup is a hand-written string.
    /// </summary>
    public static bool IsKnown(string? id)
        => !string.IsNullOrWhiteSpace(id)
            && All.Any(t => string.Equals(t, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves a stored or submitted value to the canonical id, or to <see cref="Default"/> when it
    /// is missing or no longer a theme. Canonical casing comes from <see cref="All"/>, so a row
    /// written as <c>OBSIDIAN</c> reads back as <c>obsidian</c>.
    /// </summary>
    public static string Normalize(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Default;
        }

        var trimmed = id.Trim();
        var known = All.FirstOrDefault(t => string.Equals(t, trimmed, StringComparison.OrdinalIgnoreCase));

        return known ?? Default;
    }
}
