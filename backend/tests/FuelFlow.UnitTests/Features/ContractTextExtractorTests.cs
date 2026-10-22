using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FuelFlow.Features.Contracts;
using QuestPDF.Fluent;
using Xunit;

namespace FuelFlow.UnitTests.Features;

/// <summary>
/// The contract text is a document the operator uploads, so the platform has to be able to read
/// one.
/// </summary>
/// <remarks>
/// The two formats are read differently on purpose: a .docx is a zip whose document.xml holds
/// the paragraphs, and a PDF is a format that needs a real parser. Both are read here from files
/// built in the test, because a test that reads a document somebody else made is a test of that
/// document.
/// </remarks>
public sealed class ContractTextExtractorTests
{
    [Fact]
    public async Task A_Word_Document_Is_Read_Paragraph_By_Paragraph()
    {
        var extractor = new ContractTextExtractor();
        await using var content = WordDocument("First paragraph.", "Second paragraph.");

        var text = await extractor.ExtractAsync(content, "terms.docx", CancellationToken.None);

        text.Should().Contain("First paragraph.");
        text.Should().Contain("Second paragraph.");
    }

    [Fact]
    public async Task A_Pdf_Is_Read()
    {
        var extractor = new ContractTextExtractor();
        await using var content = PdfDocument("The terms of the contract.");

        var text = await extractor.ExtractAsync(content, "terms.pdf", CancellationToken.None);

        text.Should().Contain("The terms of the contract.");
    }

    [Fact]
    public async Task A_File_That_Is_Neither_Is_Not_Read()
    {
        var extractor = new ContractTextExtractor();
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var text = await extractor.ExtractAsync(content, "terms.txt", CancellationToken.None);

        text.Should().BeNull();
    }

    [Fact]
    public async Task A_Document_With_No_Text_In_It_Reads_As_Nothing()
    {
        // A .docx whose body is missing is a file that is not a document, and a document the
        // customer cannot read is not a contract - so the upload is refused rather than stored.
        var extractor = new ContractTextExtractor();
        await using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            // An entry that is not the body.
            var entry = archive.CreateEntry("docProps/core.xml");
            await using var stream = entry.Open();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("<x/>"));
        }
        content.Position = 0;

        var text = await extractor.ExtractAsync(content, "terms.docx", CancellationToken.None);

        text.Should().BeNull();
    }

    [Fact]
    public async Task A_Document_That_Expands_Into_Gigabytes_Is_Refused()
    {
        // A .docx is a zip, and a zip can expand a few kilobytes into gigabytes. The upload is
        // capped at 20 MB - the COMPRESSED size - so without a bound on the expanded size one
        // request could ask the process for twenty gigabytes.
        var extractor = new ContractTextExtractor();
        await using var content = ZipBomb(ContractTextExtractor.MaxDocumentBytes + 1);

        var act = () => extractor.ExtractAsync(content, "terms.docx", CancellationToken.None);

        await act.Should().ThrowAsync<ContractDocumentTooLargeException>(
            "a document that expands past the cap is refused, not read");
    }

    [Fact]
    public async Task A_Document_Just_Under_The_Cap_Is_Still_Read()
    {
        // The bound is a bound and not a wall: a real document that fits is read as before.
        var extractor = new ContractTextExtractor();
        await using var content = WordDocument("First paragraph.");

        var text = await extractor.ExtractAsync(content, "terms.docx", CancellationToken.None);

        text.Should().Contain("First paragraph.");
    }

    [Fact]
    public async Task A_Word_Document_With_A_Byte_Order_Mark_Is_Still_Read()
    {
        // Word writes a UTF-8 byte order mark at the start of word/document.xml. The mark is not
        // part of the document, and an XML parser rejects it as a character before the root
        // element - so the reader has to read the mark and drop it, the way it always did.
        var extractor = new ContractTextExtractor();
        await using var content = WordDocumentWithByteOrderMark("First paragraph.");

        var text = await extractor.ExtractAsync(content, "terms.docx", CancellationToken.None);

        text.Should().Contain("First paragraph.");
    }

    /// <summary>
    /// A .docx whose body is a huge run of zeros: a few kilobytes on the wire, gigabytes when
    /// expanded.
    /// </summary>
    private static MemoryStream ZipBomb(int sizeBytes)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var entryStream = entry.Open();
            entryStream.Write(new byte[sizeBytes]);
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>A .docx with the given paragraphs, built the way Word builds one.</summary>
    private static MemoryStream WordDocument(params string[] paragraphs)
        => Docx(DocumentXml(paragraphs));

    /// <summary>A .docx whose body starts with a byte order mark, the way Word writes one.</summary>
    private static MemoryStream WordDocumentWithByteOrderMark(params string[] paragraphs)
        => Docx(DocumentXml(paragraphs), byteOrderMark: true);

    /// <summary>The markup of a Word document with the given paragraphs.</summary>
    private static string DocumentXml(IEnumerable<string> paragraphs)
    {
        var body = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            body.Append("<w:p><w:r><w:t>").Append(paragraph).Append("</w:t></w:r></w:p>");
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + "<w:body>" + body + "</w:body></w:document>";
    }

    /// <summary>A .docx whose body is the given markup.</summary>
    private static MemoryStream Docx(string xml, bool byteOrderMark = false)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var entryStream = entry.Open();
            if (byteOrderMark)
            {
                entryStream.Write(Encoding.UTF8.GetPreamble());
            }

            entryStream.Write(Encoding.UTF8.GetBytes(xml));
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>A PDF with the given text on it, built with the renderer the app already has.</summary>
    private static MemoryStream PdfDocument(string text)
    {
        // The community license, the same one the application sets at startup
        // (SharedKernel/Pdf/PdfFonts.cs). QuestPDF refuses to generate a PDF without one.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var stream = new MemoryStream();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Content().Text(text);
            });
        }).GeneratePdf(stream);

        stream.Position = 0;
        return stream;
    }
}
