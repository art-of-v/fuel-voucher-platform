using System.Reflection;

namespace FuelFlow.SharedKernel.Notifications.Email;

/// <summary>
/// FuelFlow's transactional-email brand palette and logo. Colours are lifted from the mobile app's
/// default <c>lemberg</c> theme (<c>mobile/src/core/design/themes.ts</c> / <c>palette.ts</c>) so the
/// mail matches the app: neon-green cyber-lion on black. Values are solid hex — no rgba — because the
/// Outlook/Word rendering engine ignores rgba, and no external web fonts, so a system-font stack is
/// used everywhere.
/// </summary>
internal static class EmailBrand
{
    // Canvas & surfaces (lemberg background / surface / surfaceElevated).
    public const string Canvas = "#000000";
    public const string Surface = "#0C0C0C";
    public const string SurfaceInset = "#161616";
    public const string Border = "#242424"; // ≈ lemberg border (white @ 14% on black)

    // Text (solid approximations of lemberg's translucent whites, kept legible on the dark canvas).
    public const string TextPrimary = "#FFFFFF";
    public const string TextSecondary = "#C4C4C4";
    public const string TextMuted = "#8A8A8A";

    // Accent — the lemberg brand green (primary) and its brighter neon variant (text.neon).
    public const string Accent = "#00E85F";
    public const string AccentBright = "#16FF00";

    /// <summary>Text/icon colour on an <see cref="Accent"/> fill. Deliberately dark rather than the
    /// app's <c>onPrimary</c> white: white on #00E85F is only ≈2:1, so dark-on-green is the accessible
    /// choice for the email CTA (matches the status palette's <c>success.onBase</c>).</summary>
    public const string OnAccent = "#04231A";

    /// <summary>System-font stack — no web fonts in email.</summary>
    public const string FontStack =
        "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    /// <summary>Content-Id the branded HTML references for the inline lion (<c>cid:</c>).</summary>
    public const string LogoContentId = "fuelflow-lion";

    // Embedded via an explicit LogicalName in FuelFlow.API.csproj so the name is stable regardless of
    // root namespace. Bytes are a Lanczos downscale of mobile/assets/icon.png (the real brand asset)
    // to 256px, palette-encoded (~30 KB) so every email stays well under Gmail's 102 KB clip limit.
    private const string LogoResourceName = "FuelFlow.Email.fuelflow-lion.png";

    private static readonly Lazy<byte[]?> LogoBytes = new(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(LogoResourceName);
        if (stream is null)
            return null;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    });

    /// <summary>The inline lion for the email header, or <c>null</c> if the embedded asset is somehow
    /// unavailable (the layout then falls back to the text wordmark).</summary>
    public static EmailInlineImage? LionLogo()
    {
        var bytes = LogoBytes.Value;
        return bytes is null ? null : new EmailInlineImage(LogoContentId, "image/png", bytes);
    }
}
