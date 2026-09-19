using FluentAssertions;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using System.Reflection;

namespace FuelFlow.UnitTests.Parsers;

public class QrServiceTests
{
    [Fact]
    public void GenerateQrCode_ShouldReturnEmpty_WhenPayloadIsWhitespace()
    {
        var generator = new QrGeneratorV2();

        var result = generator.GenerateQrCode("   ");

        result.Should().BeEmpty();
    }

    [Fact]
    public void GenerateQrCode_ShouldReturnBase64Png_ForValidPayload()
    {
        var generator = new QrGeneratorV2();

        var result = generator.GenerateQrCode("TEST-PAYLOAD-123", 100, 100);

        result.Should().NotBeNullOrWhiteSpace();
        var bytes = Convert.FromBase64String(result);
        bytes.Should().NotBeEmpty();
        using var image = Image.Load(bytes);
        image.Width.Should().Be(100);
        image.Height.Should().Be(100);
    }

    [Fact]
    public void GenerateQrCode_ShouldProduceDifferentOutput_ForDifferentPayloads()
    {
        var generator = new QrGeneratorV2();

        var a = generator.GenerateQrCode("payload-a", 100, 100);
        var b = generator.GenerateQrCode("payload-b", 100, 100);

        a.Should().NotBe(b);
    }

    [Fact]
    public void GenerateQrCode_ShouldHonorMaskPatternAndVersion()
    {
        var generator = new QrGeneratorV2();

        var result = generator.GenerateQrCode("1234567890", 100, 100, eccLevel: "M", version: 1, encodingMode: "NUMERIC", maskPattern: 0);

        result.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Verify_ShouldSkip_WhenOriginalMatrixIsNull()
    {
        var details = QrMatrixVerifier.Verify("payload", null, "M", 1, "BYTE", 0);

        details.Result.Should().Be(QrMatrixVerifier.VerificationResult.Skipped);
        details.SkipReason.Should().Contain("matrix");
    }

    [Fact]
    public void Verify_ShouldSkip_WhenVersionOrMaskNotExtracted()
    {
        var matrix = new bool[21, 21];

        var details = QrMatrixVerifier.Verify("payload", matrix, "M", null, "BYTE", null);

        details.Result.Should().Be(QrMatrixVerifier.VerificationResult.Skipped);
    }

    [Fact]
    public void Verify_ShouldFail_WhenMatrixSizeMismatch()
    {
        var matrix = new bool[2, 2];

        var details = QrMatrixVerifier.Verify("payload", matrix, "M", 1, "BYTE", 0);

        details.Result.Should().Be(QrMatrixVerifier.VerificationResult.Failed);
        details.MismatchPercent.Should().Be(100);
        details.SkipReason.Should().Contain("Size mismatch");
    }

    [Fact]
    public void Verify_ShouldPass_WhenMatrixMatchesRegeneratedCode()
    {
        const string payload = "HELLO-123";
        var generator = new QrGeneratorV2();
        var base64 = generator.GenerateQrCode(payload, 100, 100, "M", 1, "BYTE", 0);

        var bytes = Convert.FromBase64String(base64);
        using var image = Image.Load<Rgba32>(bytes);
        var decoder = new QrDecoder(new Mock<ILogger<QrDecoder>>().Object);
        var decoded = decoder.Decode(image);

        decoded.OriginalMatrix.Should().NotBeNull();
        decoded.Version.Should().NotBeNull();
        decoded.MaskPattern.Should().NotBeNull();

        var details = QrMatrixVerifier.Verify(
            payload,
            decoded.OriginalMatrix,
            "M",
            decoded.Version,
            "BYTE",
            decoded.MaskPattern);

        details.Result.Should().Be(QrMatrixVerifier.VerificationResult.Passed);
        details.MismatchedModules.Should().Be(0);
    }

    [Fact]
    public void Decode_ShouldReturnEmpty_WhenImageHasNoQr()
    {
        var decoder = new QrDecoder(new Mock<ILogger<QrDecoder>>().Object);
        using var blank = new Image<Rgba32>(200, 200);

        var result = decoder.Decode(blank);

        result.Text.Should().BeNull();
        result.OriginalMatrix.Should().BeNull();
    }

    [Fact]
    public void Decode_ShouldReadTextFromGeneratedQr()
    {
        const string payload = "9018$2000$;99999600000020368126=4507101299?";
        var generator = new QrGeneratorV2();
        var base64 = generator.GenerateQrCode(payload, 120, 120, "L");

        var bytes = Convert.FromBase64String(base64);
        using var image = Image.Load<Rgba32>(bytes);
        var decoder = new QrDecoder(new Mock<ILogger<QrDecoder>>().Object);

        var result = decoder.Decode(image);

        result.Text.Should().Be(payload);
    }

    [Fact]
    public void Detect_ShouldReturnEmpty_WhenNoWords()
    {
        var detector = new VoucherDetector();
        using var image = new Image<Rgba32>(200, 200);

        var page = new PageRender
        {
            PageNumber = 1,
            Image = image,
            WidthPoints = 200,
            HeightPoints = 200,
            Words = Array.Empty<Word>()
        };

        var regions = detector.Detect(page);

        regions.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ShouldGroupNearbyWordsIntoClusters()
    {
        var detector = new VoucherDetector();
        using var image = new Image<Rgba32>(400, 400);

        var clusterA = new[] { WordTestHelper.CreateWord("A1", 10, 10), WordTestHelper.CreateWord("A2", 25, 10), WordTestHelper.CreateWord("A3", 40, 10), WordTestHelper.CreateWord("A4", 55, 10), WordTestHelper.CreateWord("A5", 70, 10), WordTestHelper.CreateWord("A6", 85, 10) };
        var clusterB = new[] { WordTestHelper.CreateWord("B1", 300, 300), WordTestHelper.CreateWord("B2", 315, 300), WordTestHelper.CreateWord("B3", 330, 300), WordTestHelper.CreateWord("B4", 345, 300), WordTestHelper.CreateWord("B5", 360, 300), WordTestHelper.CreateWord("B6", 375, 300) };

        var page = new PageRender
        {
            PageNumber = 1,
            Image = image,
            WidthPoints = 400,
            HeightPoints = 400,
            Words = clusterA.Concat(clusterB).ToList()
        };

        var regions = detector.Detect(page);

        regions.Should().HaveCount(2);
    }
}

internal static class WordTestHelper
{
    public static Word CreateWord(string text, double x, double y)
    {
        var bbox = new PdfRectangle(x, y, x + 10, y + 10);
        var startPoint = new PdfPoint(x, y);
        var endPoint = new PdfPoint(x + 10, y);

        var letter = (Letter)Activator.CreateInstance(
            typeof(Letter),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new object?[]
            {
                text,
                bbox,
                startPoint,
                endPoint,
                10.0,
                10.0,
                null,
                TextRenderingMode.Fill,
                null,
                null,
                10.0,
                0
            },
            null)!;

        return new Word(new[] { letter });
    }
}

public class KloVoucherParserTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IQrDecoder> _qrDecoderMock;

    public KloVoucherParserTests()
    {
        _qrDecoderMock = new Mock<IQrDecoder>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _context.FuelTypes.AddRange(
            new FuelTypeEntity { Id = "klo-dp", Name = "ДП Mustang", StationId = "klo", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "klo-95", Name = "A 95 EURO", StationId = "klo", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = DateTime.UtcNow });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public void CanParse_ShouldReturnTrue_WhenKloKeywordPresent()
    {
        var parser = new KloVoucherParser(_context, NullLogger<KloVoucherParser>.Instance);
        var context = new ProviderDetectionContext
        {
            Words = new List<Word> { WordTestHelper.CreateWord("КЛО", 0, 0), WordTestHelper.CreateWord("KLO", 10, 0) }
        };

        var result = parser.CanParse(context);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanParse_ShouldReturnFalse_WhenKloKeywordMissing()
    {
        var parser = new KloVoucherParser(_context, NullLogger<KloVoucherParser>.Instance);
        var context = new ProviderDetectionContext
        {
            Words = new List<Word> { WordTestHelper.CreateWord("OKKO", 0, 0), WordTestHelper.CreateWord("WOG", 10, 0) }
        };

        var result = parser.CanParse(context);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_ShouldExtractVoucherFields()
    {
        var parser = new KloVoucherParser(_context, NullLogger<KloVoucherParser>.Instance);
        _qrDecoderMock.Setup(x => x.Decode(It.IsAny<Image>()))
            .Returns(new QrDecodeResult { Text = "KLO-QR-PAYLOAD", EccLevel = "L" });

        var words = new List<Word>
        {
            WordTestHelper.CreateWord("KLO", 50, 100),
            WordTestHelper.CreateWord("ДП", 60, 80),
            WordTestHelper.CreateWord("20", 70, 60),
            WordTestHelper.CreateWord("л", 85, 60),
            WordTestHelper.CreateWord("99999600000020368126", 20, 40),
            WordTestHelper.CreateWord("Дійсний", 10, 20),
            WordTestHelper.CreateWord("17.06.2026", 60, 20)
        };

        using var dummyImage = new Image<Rgba32>(100, 100);
        var pageRender = new PageRender
        {
            PageNumber = 1,
            Image = dummyImage,
            WidthPoints = 200,
            HeightPoints = 200,
            Words = words
        };
        var region = new VoucherRegion
        {
            Bounds = new Rectangle(0, 0, 100, 100),
            PdfBounds = new PdfRectangle(0, 0, 200, 200)
        };
        var context = new ProviderParseContext
        {
            PageRender = pageRender,
            VoucherRegions = new[] { region },
            QrDecoder = _qrDecoderMock.Object
        };

        var result = await parser.ParseAsync(context, CancellationToken.None);

        result.Should().HaveCount(1);
        var parsed = result.First();
        parsed.Provider.Should().Be("KLO");
        parsed.Liters.Should().Be(20m);
        parsed.ExpirationDate.Should().Be(new DateOnly(2026, 6, 17));
        parsed.VoucherNumber.Should().Be("99999600000020368126");
        parsed.QrPayload.Should().Be("KLO-QR-PAYLOAD");
        parsed.Confidence.Should().BeGreaterThan(0);
    }
}
