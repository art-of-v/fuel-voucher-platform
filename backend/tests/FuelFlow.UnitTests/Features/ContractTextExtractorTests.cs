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

    /// <summary>A .docx with the given paragraphs, built the way Word builds one.</summary>
    private static MemoryStream WordDocument(params string[] paragraphs)
    {
        var body = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            body.Append("<w:p><w:r><w:t>").Append(paragraph).Append("</w:t></w:r></w:p>");
        }

        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + "<w:body>" + body + "</w:body></w:document>";

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var entryStream = entry.Open();
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
