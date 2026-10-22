using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace FuelFlow.Features.Contracts;

/// <summary>
/// The document is larger than this reader will open.
/// </summary>
/// <remarks>
/// A .docx is a zip, and a zip can expand a few kilobytes into gigabytes. The upload is capped at
/// 20 MB - the COMPRESSED size - so without a bound on the expanded size one request could ask
/// the process for twenty gigabytes. The PDF path does not need the same guard: a PDF is not
/// compressed, so the request limit already bounds it.
/// </remarks>
public sealed class ContractDocumentTooLargeException : Exception
{
    public ContractDocumentTooLargeException(long sizeBytes)
        : base($"The document expands to {sizeBytes} bytes, which is more than this reader opens ({ContractTextExtractor.MaxDocumentBytes}).")
    {
        SizeBytes = sizeBytes;
    }

    public long SizeBytes { get; }
}

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
    /// How much text a document may carry, in bytes of the document's own markup.
    /// </summary>
    /// <remarks>
    /// A .docx is a zip, and a zip can expand a few kilobytes into gigabytes - a "zip bomb" is a
    /// small upload that fills the memory of whatever opens it. The upload itself is capped at
    /// 20 MB, which is the COMPRESSED size, so without this cap a single request could ask the
    /// process for twenty gigabytes. 16 MB of markup is far above any real contract (Word's own
    /// document.xml for a long agreement is a few hundred kilobytes) and far below what a bomb
    /// wants.
    /// </remarks>
    public const int MaxDocumentBytes = 16 * 1024 * 1024;

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

        // The entry's own length is what the archive CLAIMS, and a crafted one can claim
        // anything - so the claim is a cheap first check and the read below is the real bound.
        if (entry.Length > MaxDocumentBytes)
            throw new ContractDocumentTooLargeException(entry.Length);

        using var stream = entry.Open();
        using var buffer = new MemoryStream();

        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxDocumentBytes)
                throw new ContractDocumentTooLargeException(total);

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        // The bytes are decoded the way a reader decodes them: a byte order mark is a mark, and
        // it is not part of the document. Word writes one, and an XML parser rejects it as a
        // character before the root element - so the mark is read and dropped, as it always was.
        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
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
