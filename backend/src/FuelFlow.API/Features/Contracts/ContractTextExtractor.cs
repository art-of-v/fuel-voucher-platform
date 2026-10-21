using System.IO.Compression;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace FuelFlow.Features.Contracts;

/// <summary>
/// Reads the text out of a contract document the operator uploaded.
/// </summary>
/// <remarks>
/// <para>
/// <b>The text comes from a file, not from a text box.</b> The owner decided on 2026-10-21 that
/// the contract text is a document the operator uploads - a ready-made Word or PDF file - and
/// the platform reads it. Typing a legal document into a form is a worse way to receive one
/// than handing over the file that was written.
/// </para>
/// <para>
/// <b>What is kept is the text, and the text is what the customer reads.</b> The mobile screen
/// says "REVIEW THE TEXT", so the platform needs the text; the file's formatting is not what the
/// customer reads and is not what the audit trail asks about. The version row therefore holds
/// the text, and the version number is what ties a signature to it.
/// </para>
/// <para>
/// <b>A document with no text in it is refused, not stored.</b> A scanned PDF is a picture of a
/// document: there is nothing to read, and a contract whose text is empty is a document the
/// customer cannot review. The upload is refused with a message that says so.
/// </para>
/// <para>
/// <b>Why a Word document is read without a library.</b> A .docx is a zip whose
/// <c>word/document.xml</c> holds the paragraphs; reading it is unzipping one entry and
/// concatenating the text nodes. A PDF is a format that needs a real parser, so it gets one
/// (PdfPig) rather than a home-grown one.
/// </para>
/// </remarks>
public sealed class ContractTextExtractor
{
    /// <summary>The file extensions this reads.</summary>
    public static readonly string[] SupportedExtensions = [".docx", ".pdf"];

    /// <summary>
    /// The text of the document, or <c>null</c> when the file is not one this reads.
    /// </summary>
    /// <param name="content">The uploaded file.</param>
    /// <param name="fileName">Its name - the extension is what says what it is.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<string?> ExtractAsync(Stream content, string? fileName, CancellationToken cancellationToken)
    {
        var name = fileName ?? string.Empty;

        if (name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractDocxAsync(content, cancellationToken);
        }

        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractPdf(content);
        }

        return null;
    }

    /// <summary>
    /// The text of a Word document: the paragraphs of <c>word/document.xml</c>, in order.
    /// </summary>
    private static async Task<string?> ExtractDocxAsync(Stream content, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

        var entry = archive.GetEntry("word/document.xml");
        if (entry is null)
        {
            // A .docx without the body is a file that is not a document.
            return null;
        }

        using var reader = new StreamReader(entry.Open());
        var xml = await reader.ReadToEndAsync(cancellationToken);

        XNamespace wordprocessing = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var paragraphs = XDocument
            .Parse(xml)
            .Descendants(wordprocessing + "p")
            .Select(paragraph => string.Concat(
                paragraph.Descendants(wordprocessing + "t").Select(text => text.Value)))
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return string.Join("\n\n", paragraphs);
    }

    /// <summary>
    /// The text of a PDF. A scanned PDF has no text layer, and this returns nothing for it -
    /// which is the point: the upload is refused rather than stored as an empty document.
    /// </summary>
    private static string? ExtractPdf(Stream content)
    {
        // The same PdfPig the importer already reads vouchers with, at the version the project
        // already pins - a second PDF library in the same process would be a second answer to
        // the same question, and the two would not agree about what a PDF is.
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var bytes = buffer.ToArray();

        using var document = PdfDocument.Open(bytes);

        var pages = new List<string>(document.NumberOfPages);
        for (var i = 1; i <= document.NumberOfPages; i++)
        {
            var text = document.GetPage(i).Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                pages.Add(text.Trim());
            }
        }

        return pages.Count == 0 ? null : string.Join("\n\n", pages);
    }
}
