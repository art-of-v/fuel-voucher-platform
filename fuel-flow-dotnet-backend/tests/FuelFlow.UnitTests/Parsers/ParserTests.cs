using System.Reflection;
using FluentAssertions;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using Xunit;

namespace FuelFlow.UnitTests.Parsers;

public class ParserTests : IDisposable
{
    private readonly Mock<IQrDecoder> _qrDecoderMock;
    private readonly ApplicationDbContext _context;

    public ParserTests()
    {
        _qrDecoderMock = new Mock<IQrDecoder>();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        SeedFuelTypes();
    }

    private void SeedFuelTypes()
    {
        var fuelTypes = new[]
        {
            new FuelFlow.Features.Stations.SharedModels.FuelTypeEntity { Id = "okko-dp", Name = "ДП ЄВРО", StationId = "okko", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = DateTime.UtcNow },
            new FuelFlow.Features.Stations.SharedModels.FuelTypeEntity { Id = "okko-95", Name = "A-95", StationId = "okko", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = DateTime.UtcNow },
            new FuelFlow.Features.Stations.SharedModels.FuelTypeEntity { Id = "wog-dp", Name = "ДП Mustang", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = DateTime.UtcNow },
            new FuelFlow.Features.Stations.SharedModels.FuelTypeEntity { Id = "wog-95", Name = "A-95 Mustang", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = DateTime.UtcNow }
        };
        _context.FuelTypes.AddRange(fuelTypes);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public void CanParse_ShouldReturnTrue_WhenOkkoKeywordIsPresent()
    {
        // Arrange
        var parser = new OkkoVoucherParser(_context);
        var words = new List<Word>
        {
            CreateWord("Welcome", 0, 0),
            CreateWord("to", 10, 0),
            CreateWord("OKKO", 20, 0),
            CreateWord("Station", 30, 0)
        };
        var context = new ProviderDetectionContext { Words = words };

        // Act
        var result = parser.CanParse(context);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanParse_ShouldReturnFalse_WhenOkkoKeywordIsMissing()
    {
        // Arrange
        var parser = new OkkoVoucherParser(_context);
        var words = new List<Word>
        {
            CreateWord("Welcome", 0, 0),
            CreateWord("to", 10, 0),
            CreateWord("WOG", 20, 0)
        };
        var context = new ProviderDetectionContext { Words = words };

        // Act
        var result = parser.CanParse(context);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_ShouldExtractCorrectFields_WithVaryingUkrainianFuelTypesAndLiters()
    {
        // Arrange
        var parser = new OkkoVoucherParser(_context);
        _qrDecoderMock.Setup(x => x.Decode(It.IsAny<Image>()))
            .Returns(new QrDecodeResult { Text = "9018$2000$;99999600000020368126=4507101299?", EccLevel = "L" });

        // Setup words that mimic a single OKKO voucher:
        // Fuel: "ДП ЄВРО"
        // Liters: "20 л"
        // Expiry: "Дійсний до 17.06.2025 включно"
        // Voucher Number: "99999600000020368126"
        var words = new List<Word>
        {
            CreateWord("OKKO", 50, 100),
            CreateWord("ДП", 60, 80),
            CreateWord("ЄВРО", 80, 80),
            CreateWord("20", 70, 60),
            CreateWord("л", 85, 60),
            CreateWord("Дійсний", 10, 40),
            CreateWord("до", 50, 40),
            CreateWord("17.06.2025", 70, 40),
            CreateWord("99999600000020368126", 20, 20)
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
            PdfBounds = new PdfRectangle(0, 0, 200, 200) // Covers all words
        };

        var context = new ProviderParseContext
        {
            PageRender = pageRender,
            VoucherRegions = new[] { region },
            QrDecoder = _qrDecoderMock.Object
        };

        // Act
        var result = await parser.ParseAsync(context, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        var parsed = result.First();
        parsed.Provider.Should().Be("OKKO");
        parsed.FuelTypeId.Should().Be("okko-dp");
        parsed.Liters.Should().Be(20m);
        parsed.ExpirationDate.Should().Be(new DateOnly(2025, 6, 17));
        parsed.VoucherNumber.Should().Be("99999600000020368126");
        parsed.QrPayload.Should().Be("9018$2000$;99999600000020368126=4507101299?");
        parsed.Confidence.Should().Be(100m);
    }

    [Fact]
    public async Task ParseAsync_ShouldHandleLowerConfidence_WhenSomeFieldsAreMissing()
    {
        // Arrange
        var parser = new OkkoVoucherParser(_context);
        _qrDecoderMock.Setup(x => x.Decode(It.IsAny<Image>()))
            .Returns(new QrDecodeResult());

        var words = new List<Word>
        {
            CreateWord("OKKO", 10, 100),
            CreateWord("ГАЗ", 10, 80),
            CreateWord("50 л", 10, 60),
            CreateWord("99999600000020368126", 10, 40)
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

        // Act
        var result = await parser.ParseAsync(context, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        var parsed = result.First();
        parsed.FuelTypeId.Should().Be("okko-95");
        parsed.Liters.Should().Be(50m);
        parsed.VoucherNumber.Should().Be("99999600000020368126");
        parsed.ExpirationDate.Should().Be(default);
        parsed.QrPayload.Should().Be(string.Empty);
        parsed.Confidence.Should().Be(60m);
    }

    [Fact]
    public void CanParse_ShouldReturnTrue_WhenWogKeywordIsPresent()
    {
        var parser = new WogVoucherParser(_context);
        var words = new List<Word>
        {
            CreateWord("10л", 0, 0),
            CreateWord("A-95", 10, 0),
            CreateWord("WOG", 20, 0)
        };
        var context = new ProviderDetectionContext { Words = words };

        var result = parser.CanParse(context);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanParse_ShouldReturnFalse_WhenWogKeywordIsMissing()
    {
        var parser = new WogVoucherParser(_context);
        var words = new List<Word>
        {
            CreateWord("OKKO", 0, 0),
            CreateWord("A-95", 10, 0)
        };
        var context = new ProviderDetectionContext { Words = words };

        var result = parser.CanParse(context);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_Wog_ShouldExtractCorrectFields()
    {
        var parser = new WogVoucherParser(_context);
        _qrDecoderMock.Setup(x => x.Decode(It.IsAny<Image>()))
            .Returns(new QrDecodeResult { Text = "10094100096856672796", EccLevel = "L" });

        var words = new List<Word>
        {
            CreateWord("10", 70, 100),
            CreateWord("л", 85, 100),
            CreateWord("A-95", 50, 80),
            CreateWord("10094100096856672796", 20, 60),
            CreateWord("Дійсний", 10, 40),
            CreateWord("до", 50, 40),
            CreateWord("13.05.2026", 70, 40),
            CreateWord("WOG", 40, 20)
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
        parsed.Provider.Should().Be("WOG");
        parsed.FuelTypeId.Should().Be("wog-95");
        parsed.Liters.Should().Be(10m);
        parsed.ExpirationDate.Should().Be(new DateOnly(2026, 5, 13));
        parsed.VoucherNumber.Should().Be("10094100096856672796");
        parsed.Confidence.Should().Be(100m);
    }

    [Fact]
    public async Task ParseAsync_Wog_ShouldHandleUnknownFuelType()
    {
        var parser = new WogVoucherParser(_context);
        _qrDecoderMock.Setup(x => x.Decode(It.IsAny<Image>()))
            .Returns(new QrDecodeResult());

        var words = new List<Word>
        {
            CreateWord("WOG", 0, 100),
            CreateWord("20", 70, 80),
            CreateWord("л", 85, 80),
            CreateWord("99999600000020368126", 20, 60)
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
        parsed.Provider.Should().Be("WOG");
        parsed.FuelTypeId.Should().Be("wog-dp");
        parsed.Liters.Should().Be(20m);
        parsed.VoucherNumber.Should().Be("99999600000020368126");
        parsed.Confidence.Should().Be(60m);
    }

    private static Word CreateWord(string text, double x, double y)
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
                text, // value
                bbox, // glyphRectangle
                startPoint, // startBaseLine
                endPoint, // endBaseLine
                10.0, // width
                10.0, // fontSize
                null, // font
                TextRenderingMode.Fill, // renderingMode
                null, // strokeColor
                null, // fillColor
                10.0, // pointSize
                0 // textSequence
            },
            null)!;

        return new Word(new[] { letter });
    }
}

