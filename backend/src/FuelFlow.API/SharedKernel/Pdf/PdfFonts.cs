using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace FuelFlow.SharedKernel.Pdf;

/// <summary>
/// The fonts a generated document uses, registered once per process.
/// </summary>
/// <remarks>
/// <para>
/// QuestPDF has no fonts of its own: it resolves them from the host. In a Linux container that means
/// whatever the image happens to carry, and on a bare ubuntu runner that is frequently no usable
/// Cyrillic face at all. A missing glyph does not fail loudly - it renders as nothing - which on a
/// customer-facing receipt would silently drop the product name and the issuer's name.
/// </para>
/// <para>
/// So the faces ship with the assembly and are registered explicitly. PT Sans was chosen because it
/// was drawn for Cyrillic and carries the Ukrainian letters (і, ї, є, ґ) that many Latin-first faces
/// lack, along with the hryvnia sign. Both weights are verified by a test that reads the font's own
/// character map, so a future font swap cannot quietly lose a letter.
/// </para>
/// </remarks>
public static class PdfFonts
{
    private static readonly object Gate = new();
    private static bool _registered;

    /// <summary>
    /// The family name PT Sans registers under. QuestPDF names a registered face by the font's own
    /// family, not by an alias the caller chooses, so the document has to ask for this string.
    /// </summary>
    public const string Family = "PT Sans";

    public static void EnsureRegistered()
    {
        // QuestPDF refuses to render until a license type is declared, and deliberately so: the choice
        // is a commercial decision, not a default. It is set here - the single entry point to PDF
        // generation - rather than in service registration, so that a background job, an integration
        // test or a new host cannot generate a document with the license undeclared.
        //
        // Community is free while annual gross revenue is under USD 1,000,000 and the company is not
        // publicly traded. If either changes, this line is the only thing that has to change; the
        // generated documents are unaffected.
        QuestPDF.Settings.License = LicenseType.Community;

        if (_registered)
        {
            return;
        }

        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            using var regular = OpenFont("PT_Sans-Web-Regular.ttf");
            using var bold = OpenFont("PT_Sans-Web-Bold.ttf");
            FontManager.RegisterFontFromStream(regular);
            FontManager.RegisterFontFromStream(bold);

            _registered = true;
        }
    }

    private static Stream OpenFont(string fileName)
        => typeof(PdfFonts).Assembly.GetManifestResourceStream($"FuelFlow.API.Resources.Fonts.{fileName}")
           ?? throw new InvalidOperationException(
               $"Embedded font {fileName} is missing from the assembly - a customer document would render without its text.");
}