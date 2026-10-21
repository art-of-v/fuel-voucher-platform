using FuelFlow.Features.Brands;
using System.Reflection;
using FluentAssertions;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

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

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _context.AddMissing();
        SeedFuelTypes();
    }

    private void SeedFuelTypes()
    {
        var fuelTypes = new[]
        {
            new FuelTypeEntity { Id = ToUuid("okko-dp"), Name = "ДП ЄВРО", BrandId = BrandIds.Okko, BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = TestClock.UtcNow },
            new FuelTypeEntity { Id = ToUuid("okko-95"), Name = "A-95", BrandId = BrandIds.Okko, BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = TestClock.UtcNow },
            new FuelTypeEntity { Id = ToUuid("wog-dp"), Name = "ДП Mustang", BrandId = BrandIds.Wog, BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = TestClock.UtcNow },
            new FuelTypeEntity { Id = ToUuid("wog-95"), Name = "A-95 Mustang", BrandId = BrandIds.Wog, BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = TestClock.UtcNow },
            new FuelTypeEntity { Id = ToUuid("wog-95-euro"), Name = "A 95 EURO", BrandId = BrandIds.Wog, BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = TestClock.UtcNow }
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
        var parser = new OkkoVoucherParser(_context, new BrandSlugResolver(_context), NullLogger<OkkoVoucherParser>.Instance);
        var words = new List<Word>
        {
            CreateWord("Welcome", 0, 0),
            CreateWord("to", 10, 0),
            CreateWord("okko", 20, 0),
            CreateWord("Brand", 30, 0)
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
        var parser = new OkkoVoucherParser(_context, new BrandSlugResolver(_context), NullLogger<OkkoVoucherParser>.Instance);
        var words = new List<Word>
        {
            CreateWord("Welcome", 0, 0),
            CreateWord("to", 10, 0),
            CreateWord("wog", 20, 0)
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
        var parser = new OkkoVoucherParser(_context, new BrandSlugResolver(_context), NullLogger<OkkoVoucherParser>.Instance);
        _qrDecoderMock.Setup(x => x.Decode(It.IsAny<Image>()))
            .Returns(new QrDecodeResult { Text = "9018$2000$;99999600000020368126=4507101299?", EccLevel = "L" });

        // Setup words that mimic a single OKKO voucher:
        // Fuel: "ДП ЄВРО"
        // Liters: "20 л"
        // Expiry: "Дійсний до 17.06.2025 включно"
        // Voucher Number: "99999600000020368126"
        var words = new List<Word>
        {
            CreateWord("okko", 50, 100),
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
        parsed.Brand.Should().Be("okko");
        parsed.FuelTypeId.Should().Be(ToUuid("okko-dp"));
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
        var parser = new OkkoVoucherParser(_context, new BrandSlugResolver(_context), NullLogger<OkkoVoucherParser>.Instance);
        _qrDecoderMock.Setup(x => x.Decode(It.IsAny<Image>()))
            .Returns(new QrDecodeResult());

        var words = new List<Word>
        {
            CreateWord("okko", 10, 100),
            CreateWord("VOUCHER", 10, 80),
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
        parsed.FuelTypeId.Should().BeNull();
        parsed.Liters.Should().Be(50m);
        parsed.VoucherNumber.Should().Be("99999600000020368126");
        parsed.ExpirationDate.Should().Be(default);
        parsed.QrPayload.Should().Be(string.Empty);
        parsed.Confidence.Should().Be(40m);
    }

    [Fact]
    public void CanParse_ShouldReturnTrue_WhenWogKeywordIsPresent()
    {
        var parser = new WogVoucherParser(_context, new BrandSlugResolver(_context), NullLogger<WogVoucherParser>.Instance);
        var words = new List<Word>
        {
            CreateWord("10л", 0, 0),
            CreateWord("A-95", 10, 0),
            CreateWord("wog", 20, 0)
        };
        var context = new ProviderDetectionContext { Words = words };

        var result = parser.CanParse(context);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanParse_ShouldReturnFalse_WhenWogKeywordIsMissing()
    {
        var parser = new WogVoucherParser(_context, new BrandSlugResolver(_context), NullLogger<WogVoucherParser>.Instance);
        var words = new List<Word>
        {
            CreateWord("okko", 0, 0),
            CreateWord("A-95", 10, 0)
        };
        var context = new ProviderDetectionContext { Words = words };

        var result = parser.CanParse(context);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_Wog_ShouldExtractCorrectFields()
    {
        var parser = new WogVoucherParser(_context, new BrandSlugResolver(_context), NullLogger<WogVoucherParser>.Instance);
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
            CreateWord("wog", 40, 20)
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
        parsed.Brand.Should().Be("wog");
        parsed.FuelTypeId.Should().Be(ToUuid("wog-95-euro"));
        parsed.Liters.Should().Be(10m);
        parsed.ExpirationDate.Should().Be(new DateOnly(2026, 5, 13));
        parsed.VoucherNumber.Should().Be("10094100096856672796");
        parsed.Confidence.Should().Be(100m);
    }

    [Fact]
    public async Task ParseAsync_Wog_ShouldHandleUnknownFuelType()
    {
        var parser = new WogVoucherParser(_context, new BrandSlugResolver(_context), NullLogger<WogVoucherParser>.Instance);
        _qrDecoderMock.Setup(x => x.Decode(It.IsAny<Image>()))
            .Returns(new QrDecodeResult());

        var words = new List<Word>
        {
            CreateWord("wog", 0, 100),
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

        // Regression: an unresolved WOG fuel type must yield a null FuelTypeId (a graceful
        // per-row rejection downstream), NOT a hardcoded "wog-dp" fallback. That fallback both
        // mislabelled the voucher as diesel and, when "wog-dp" was absent from a drifted
        // production catalog, tripped the fuel_type_id foreign key and 500'd the whole import.
        result.Should().HaveCount(1);
        var parsed = result.First();
        parsed.Brand.Should().Be("wog");
        parsed.FuelTypeId.Should().BeNull();
        parsed.Liters.Should().Be(20m);
        parsed.VoucherNumber.Should().Be("99999600000020368126");
        parsed.Confidence.Should().Be(40m);
    }

    [Fact]
    public async Task ParseAsync_Wog_A95_ResolvesAgainstDriftedCyrillicProductionName()
    {
        // Regression: production WOG catalog names drift from the seed (Cyrillic "А", hyphen,
        // "ЄВРО" suffix). The old exact-name match rejected every such row and fell back to a
        // hardcoded "wog-dp" id; category-based resolution must resolve a Latin "A-95" voucher
        // against the drifted standard-95 row ("А-95 ЄВРО") — and to the standard (EURO) grade,
        // not the "A-95 Mustang" premium row, since the voucher carries no Mustang marker.
        using var context = CreateContextWith(
            new FuelTypeEntity { Id = ToUuid("wog-95-euro"), Name = "А-95 ЄВРО", BrandId = BrandIds.Wog, BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = TestClock.UtcNow },
            new FuelTypeEntity { Id = ToUuid("wog-95"), Name = "A-95 Mustang", BrandId = BrandIds.Wog, BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = TestClock.UtcNow },
            new FuelTypeEntity { Id = ToUuid("wog-dp"), Name = "ДП Mustang", BrandId = BrandIds.Wog, BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = TestClock.UtcNow });

        var parser = new WogVoucherParser(context, new BrandSlugResolver(context), NullLogger<WogVoucherParser>.Instance);
        _qrDecoderMock.Setup(x => x.Decode(It.IsAny<Image>()))
            .Returns(new QrDecodeResult { Text = "10094200095062108351", EccLevel = "L" });

        var words = new List<Word>
        {
            CreateWord("wog", 40, 100),
            CreateWord("A-95", 50, 80),
            CreateWord("20", 70, 60),
            CreateWord("л", 85, 60),
            CreateWord("09.02.2026", 70, 40),
            CreateWord("10094200095062108351", 20, 20)
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
        var context2 = new ProviderParseContext
        {
            PageRender = pageRender,
            VoucherRegions = new[] { region },
            QrDecoder = _qrDecoderMock.Object
        };

        var result = await parser.ParseAsync(context2, CancellationToken.None);

        result.Should().HaveCount(1);
        var parsed = result.First();
        parsed.Brand.Should().Be("wog");
        parsed.FuelTypeId.Should().Be(ToUuid("wog-95-euro"));
        parsed.Liters.Should().Be(20m);
        parsed.ExpirationDate.Should().Be(new DateOnly(2026, 2, 9));
        parsed.VoucherNumber.Should().Be("10094200095062108351");
        parsed.Confidence.Should().Be(100m);
    }

    [Fact]
    public async Task ParseAsync_Okko_A95_ResolvesAgainstCyrillicProductionName_ViaQrCode()
    {
        // Regression for the production incident: OKKO catalog has "А95 ЄВРО" (Cyrillic А,
        // no hyphen, EURO suffix). The QR product code 9015 (A-95) must resolve it, whereas
        // the old hardcoded-string match against "A-95" (Latin) rejected every such voucher.
        using var context = CreateContextWith(
            new FuelTypeEntity { Id = ToUuid("okko-95"), Name = "А95 ЄВРО", BrandId = BrandIds.Okko, BasePrice = 54, DiscountPrice = 51, CreatedAtUtc = TestClock.UtcNow },
            new FuelTypeEntity { Id = ToUuid("okko-dp"), Name = "ДП ЄВРО", BrandId = BrandIds.Okko, BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = TestClock.UtcNow });

        var parser = new OkkoVoucherParser(context, new BrandSlugResolver(context), NullLogger<OkkoVoucherParser>.Instance);
        _qrDecoderMock.Setup(x => x.Decode(It.IsAny<Image>()))
            .Returns(new QrDecodeResult { Text = "9015$2000$;99999600000020368126=4507101299?", EccLevel = "L" });

        var words = new List<Word>
        {
            CreateWord("okko", 50, 100),
            CreateWord("20", 70, 60),
            CreateWord("л", 85, 60),
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
            PdfBounds = new PdfRectangle(0, 0, 200, 200)
        };
        var context2 = new ProviderParseContext
        {
            PageRender = pageRender,
            VoucherRegions = new[] { region },
            QrDecoder = _qrDecoderMock.Object
        };

        var result = await parser.ParseAsync(context2, CancellationToken.None);

        result.Should().HaveCount(1);
        var parsed = result.First();
        parsed.FuelTypeId.Should().Be(ToUuid("okko-95"));
        parsed.Confidence.Should().Be(100m);
    }

    private static ApplicationDbContext CreateContextWith(params FuelTypeEntity[] fuelTypes)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options, TestQrCipher.Instance);
        // The parsers resolve the network they are reading through BrandSlugResolver, so a fixture
        // that names BrandIds.Okko on a fuel has to have that network in the catalogue.
        context.AddMissing();
        context.FuelTypes.AddRange(fuelTypes);
        context.SaveChanges();
        return context;
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

