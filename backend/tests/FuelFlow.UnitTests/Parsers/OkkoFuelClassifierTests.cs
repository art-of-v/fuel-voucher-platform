using FluentAssertions;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.SharedKernel.Domain;
using Xunit;

namespace FuelFlow.UnitTests.Parsers;

public class OkkoFuelClassifierTests
{
    // Production-like OKKO catalog: Cyrillic "А95 ЄВРО" (Cyrillic А, no hyphen, EURO suffix)
    // and "ДП ЄВРО" — the exact rows that caused every non-diesel voucher to be rejected.
    private static readonly FuelTypeEntity[] ProductionOkkoFuelTypes =
    [
        new() { Id = "okko-95", Name = "А95 ЄВРО", StationId = "okko", BasePrice = 54, DiscountPrice = 51 },
        new() { Id = "okko-dp", Name = "ДП ЄВРО", StationId = "okko", BasePrice = 55, DiscountPrice = 52 }
    ];

    // Full seed catalog, to prove premium (PULLS) variants stay distinct from their base grades.
    private static readonly FuelTypeEntity[] SeedOkkoFuelTypes =
    [
        new() { Id = "okko-dp", Name = "ДП ЄВРО", StationId = "okko", BasePrice = 55, DiscountPrice = 52 },
        new() { Id = "okko-95", Name = "A-95", StationId = "okko", BasePrice = 54, DiscountPrice = 51 },
        new() { Id = "okko-p95", Name = "Pulls 95", StationId = "okko", BasePrice = 60, DiscountPrice = 56 },
        new() { Id = "okko-pulls-dp", Name = "ДП PULLS", StationId = "okko", BasePrice = 58, DiscountPrice = 55 },
        new() { Id = "okko-gas", Name = "ГАЗ", StationId = "okko", BasePrice = 29, DiscountPrice = 27 }
    ];

    [Theory]
    [InlineData("9018", OkkoFuelCategory.Diesel)]
    [InlineData("9518", OkkoFuelCategory.Diesel)]
    [InlineData("45290", OkkoFuelCategory.DieselPulls)]
    [InlineData("9015", OkkoFuelCategory.A95)]
    [InlineData("9016", OkkoFuelCategory.A95)]
    [InlineData("9515", OkkoFuelCategory.A95)]
    [InlineData("9009", OkkoFuelCategory.A95)]
    [InlineData("9019", OkkoFuelCategory.Gas)]
    [InlineData("9020", OkkoFuelCategory.Gas)]
    public void CategoryFromQrCode_ShouldMapKnownCodes(string code, OkkoFuelCategory expected)
    {
        OkkoFuelClassifier.CategoryFromQrCode(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("0000")]
    [InlineData("123")]
    [InlineData(null)]
    public void CategoryFromQrCode_ShouldReturnNull_ForUnknownCode(string? code)
    {
        // Must NOT silently coerce unknown codes to A-95.
        OkkoFuelClassifier.CategoryFromQrCode(code).Should().BeNull();
    }

    [Theory]
    [InlineData("9018$2000$;99999600000020368126=4507101299?", "9018")]
    [InlineData("45290$1000$;123=456?", "45290")]
    [InlineData("10094100096856672796", null)] // WOG-style plain number, no product code
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TryGetProductCode_ShouldExtractLeadingCode(string? payload, string? expected)
    {
        OkkoFuelClassifier.TryGetProductCode(payload).Should().Be(expected);
    }

    [Theory]
    // Latin, Cyrillic, hyphen and suffix variants all resolve to the same A95 category.
    [InlineData("A-95", OkkoFuelCategory.A95)]
    [InlineData("А95", OkkoFuelCategory.A95)]         // Cyrillic А
    [InlineData("А95 ЄВРО", OkkoFuelCategory.A95)]    // Cyrillic + EURO suffix (production)
    [InlineData("A 95 EURO", OkkoFuelCategory.A95)]
    [InlineData("ДП ЄВРО", OkkoFuelCategory.Diesel)]
    [InlineData("ДП", OkkoFuelCategory.Diesel)]
    [InlineData("Pulls 95", OkkoFuelCategory.A95Pulls)]
    [InlineData("ДП PULLS", OkkoFuelCategory.DieselPulls)]
    [InlineData("ГАЗ", OkkoFuelCategory.Gas)]
    public void CategoryFromName_ShouldClassifyDisplayNameVariants(string name, OkkoFuelCategory expected)
    {
        OkkoFuelClassifier.CategoryFromName(name).Should().Be(expected);
    }

    [Fact]
    public void CategoryFromName_ShouldReturnNull_ForUnrelatedText()
    {
        OkkoFuelClassifier.CategoryFromName("Some Unknown Product").Should().BeNull();
    }

    [Theory]
    // Raw OCR text: a bare "95" inside a voucher number must NOT be read as petrol.
    [InlineData("A-95\n20 л\n99999600000020368126", OkkoFuelCategory.A95)]
    [InlineData("ДП ЄВРО\n20 л", OkkoFuelCategory.Diesel)]
    [InlineData("ГАЗ\n30 л", OkkoFuelCategory.Gas)]
    [InlineData("OKKO\n99999600000020368126", null)]  // digits contain 95 but no A marker
    public void CategoryFromText_ShouldAnchorPetrolToAMarker(string text, OkkoFuelCategory? expected)
    {
        OkkoFuelClassifier.CategoryFromText(text).Should().Be(expected);
    }

    [Fact]
    public void ResolveFuelType_A95ViaQrCode_ResolvesAgainstCyrillicProductionName()
    {
        // The production regression: QR code 9015 (A-95) against a DB row named "А95 ЄВРО".
        var result = OkkoFuelClassifier.ResolveFuelType(
            rawText: "OKKO\n20 л\n99999600000020368126",
            qrPayload: "9015$2000$;99999600000020368126=4507101299?",
            ProductionOkkoFuelTypes);

        result.Should().NotBeNull();
        result!.Id.Should().Be("okko-95");
    }

    [Fact]
    public void ResolveFuelType_DieselViaQrCode_Resolves()
    {
        var result = OkkoFuelClassifier.ResolveFuelType(
            rawText: "OKKO",
            qrPayload: "9018$2000$;99999600000020368126=4507101299?",
            ProductionOkkoFuelTypes);

        result.Should().NotBeNull();
        result!.Id.Should().Be("okko-dp");
    }

    [Fact]
    public void ResolveFuelType_GasViaQrCode_Resolves()
    {
        var result = OkkoFuelClassifier.ResolveFuelType(
            rawText: "OKKO",
            qrPayload: "9019$2000$;123=456?",
            SeedOkkoFuelTypes);

        result.Should().NotBeNull();
        result!.Id.Should().Be("okko-gas");
    }

    [Fact]
    public void ResolveFuelType_PullsCodeStaysDistinctFromBaseDiesel()
    {
        var result = OkkoFuelClassifier.ResolveFuelType(
            rawText: "OKKO",
            qrPayload: "45290$2000$;123=456?",
            SeedOkkoFuelTypes);

        result.Should().NotBeNull();
        result!.Id.Should().Be("okko-pulls-dp");
    }

    [Fact]
    public void ResolveFuelType_FallsBackToText_WhenQrAbsent()
    {
        // No QR payload — resolution must fall back to the OCR text.
        var result = OkkoFuelClassifier.ResolveFuelType(
            rawText: "OKKO\nА95 ЄВРО\n20 л\n99999600000020368126",
            qrPayload: null,
            ProductionOkkoFuelTypes);

        result.Should().NotBeNull();
        result!.Id.Should().Be("okko-95");
    }

    [Fact]
    public void ResolveFuelType_ReturnsNull_ForUnmappedQrCode_WhenTextHasNoSignal()
    {
        // Unknown code and no fuel signal in text -> unresolved (row will be rejected), never a silent default.
        var result = OkkoFuelClassifier.ResolveFuelType(
            rawText: "OKKO\n20 л\n99999600000020368126",
            qrPayload: "0000$2000$;99999600000020368126=4507101299?",
            ProductionOkkoFuelTypes);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveFuelType_ReturnsNull_WhenCategoryNotInCatalog()
    {
        // Gas QR code but the production catalog has no gas row -> unresolved, not mis-assigned.
        var result = OkkoFuelClassifier.ResolveFuelType(
            rawText: "OKKO",
            qrPayload: "9019$2000$;123=456?",
            ProductionOkkoFuelTypes);

        result.Should().BeNull();
    }
}
