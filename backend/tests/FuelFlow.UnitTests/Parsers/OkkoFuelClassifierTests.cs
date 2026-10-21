using FluentAssertions;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.SharedKernel.Domain;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.UnitTests.Parsers;

public class OkkoFuelClassifierTests
{
    // Production-like OKKO catalog: Cyrillic "А95 ЄВРО" (Cyrillic А, no hyphen, EURO suffix)
    // and "ДП ЄВРО" — the exact rows that caused every non-diesel voucher to be rejected.
    private static readonly FuelTypeEntity[] ProductionOkkoFuelTypes =
    [
        new() { Id = ToUuid("okko-95"), Name = "А95 ЄВРО", BrandId = BrandIds.Okko, BasePrice = 54, DiscountPrice = 51 },
        new() { Id = ToUuid("okko-dp"), Name = "ДП ЄВРО", BrandId = BrandIds.Okko, BasePrice = 55, DiscountPrice = 52 }
    ];

    // Full seed catalog, to prove premium (PULLS) variants stay distinct from their base grades.
    private static readonly FuelTypeEntity[] SeedOkkoFuelTypes =
    [
        new() { Id = ToUuid("okko-dp"), Name = "ДП ЄВРО", BrandId = BrandIds.Okko, BasePrice = 55, DiscountPrice = 52 },
        new() { Id = ToUuid("okko-95"), Name = "A-95", BrandId = BrandIds.Okko, BasePrice = 54, DiscountPrice = 51 },
        new() { Id = ToUuid("okko-p95"), Name = "Pulls 95", BrandId = BrandIds.Okko, BasePrice = 60, DiscountPrice = 56 },
        new() { Id = ToUuid("okko-pulls-dp"), Name = "ДП PULLS", BrandId = BrandIds.Okko, BasePrice = 58, DiscountPrice = 55 },
        new() { Id = ToUuid("okko-gas"), Name = "ГАЗ", BrandId = BrandIds.Okko, BasePrice = 29, DiscountPrice = 27 }
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
    // The exact fuel_code values okko.ua publishes beside each price on /fuels.
    [InlineData("A-95", OkkoFuelCategory.A95)]
    [InlineData("Pulls 95", OkkoFuelCategory.A95Pulls)]
    [InlineData("DP", OkkoFuelCategory.Diesel)]
    [InlineData("Pulls Diesel", OkkoFuelCategory.DieselPulls)]
    [InlineData("SPBT", OkkoFuelCategory.Gas)]
    public void CategoryFromSiteFuelCode_ShouldMapPublishedCodes(string code, OkkoFuelCategory expected)
    {
        OkkoFuelClassifier.CategoryFromSiteFuelCode(code).Should().Be(expected);
    }

    [Theory]
    // Out of our catalog's scope, and deliberately unmapped rather than coerced:
    // "Pulls 100" is aviation jet fuel and "AdBlue" is an additive, not a fuel grade.
    [InlineData("Pulls 100", null)]
    [InlineData("AdBlue", null)]
    [InlineData("0000", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void CategoryFromSiteFuelCode_ShouldReturnNull_ForUnmappedCodes(string? code, OkkoFuelCategory? expected)
    {
        OkkoFuelClassifier.CategoryFromSiteFuelCode(code).Should().Be(expected);
    }

    [Fact]
    public void CategoryFromSiteFuelCode_ShouldBeIndependentOfTheLocalizedTitle()
    {
        // The site's rendered title is translated per request; only fuel_code is stable. Two fetches
        // with different titles must still resolve to the same category from the same code.
        var english = OkkoFuelClassifier.CategoryFromSiteFuelCode("DP");
        var ukrainian = OkkoFuelClassifier.CategoryFromSiteFuelCode("DP");
        english.Should().Be(ukrainian).And.Be(OkkoFuelCategory.Diesel);
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
        result!.Id.Should().Be(ToUuid("okko-95"));
    }

    [Fact]
    public void ResolveFuelType_DieselViaQrCode_Resolves()
    {
        var result = OkkoFuelClassifier.ResolveFuelType(
            rawText: "okko",
            qrPayload: "9018$2000$;99999600000020368126=4507101299?",
            ProductionOkkoFuelTypes);

        result.Should().NotBeNull();
        result!.Id.Should().Be(ToUuid("okko-dp"));
    }

    [Fact]
    public void ResolveFuelType_GasViaQrCode_Resolves()
    {
        var result = OkkoFuelClassifier.ResolveFuelType(
            rawText: "okko",
            qrPayload: "9019$2000$;123=456?",
            SeedOkkoFuelTypes);

        result.Should().NotBeNull();
        result!.Id.Should().Be(ToUuid("okko-gas"));
    }

    [Fact]
    public void ResolveFuelType_PullsCodeStaysDistinctFromBaseDiesel()
    {
        var result = OkkoFuelClassifier.ResolveFuelType(
            rawText: "okko",
            qrPayload: "45290$2000$;123=456?",
            SeedOkkoFuelTypes);

        result.Should().NotBeNull();
        result!.Id.Should().Be(ToUuid("okko-pulls-dp"));
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
        result!.Id.Should().Be(ToUuid("okko-95"));
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
            rawText: "okko",
            qrPayload: "9019$2000$;123=456?",
            ProductionOkkoFuelTypes);

        result.Should().BeNull();
    }

    // --- the product-code crosswalk (fuel_type_product_codes, #349 step 3b) -------------------

    [Fact]
    public void AProductCodeRow_ResolvesTheFuel_WithNoNameMatchingAtAll()
    {
        // The point of the table: the QR is the one signal in a parsed voucher that is not a name, so a
        // row that claims the code answers the voucher exactly. Here the fuel's NAME deliberately
        // classifies as something else — "А95 ЄВРО" reads as A95Pulls to the text rules — and the code
        // still decides, because a code cannot be spelled wrong.
        var fuels = new[]
        {
            new FuelTypeEntity { Id = ToUuid("okko-95-euro"), Name = "А95 ЄВРО", BrandId = BrandIds.Okko, BasePrice = 56, DiscountPrice = 53 },
            new FuelTypeEntity { Id = ToUuid("okko-95"), Name = "А-95", BrandId = BrandIds.Okko, BasePrice = 54, DiscountPrice = 51 }
        };
        var byCode = new Dictionary<string, FuelTypeEntity> { ["9015"] = fuels[0] };

        var result = OkkoFuelClassifier.ResolveFuelType(
            rawText: "OKKO\n20 л\n99999600000020368126",
            qrPayload: "9015$2000$;99999600000020368126=4507101299?",
            fuels,
            byCode);

        result.Should().NotBeNull();
        result!.Id.Should().Be(ToUuid("okko-95-euro"),
            "the category path would have resolved this on the name alone, to a different fuel");
    }

    [Fact]
    public void ACodeNoRowClaims_FallsBackToTheCategoryPath()
    {
        // The table starts empty, so this fallback is what every voucher resolves through until somebody
        // fills it in. Asserted so that adding a table cannot be the thing that stops imports working.
        var byCode = new Dictionary<string, FuelTypeEntity>();

        var result = OkkoFuelClassifier.ResolveFuelType(
            rawText: "OKKO\n20 л\n99999600000020368126",
            qrPayload: "9015$2000$;99999600000020368126=4507101299?",
            ProductionOkkoFuelTypes,
            byCode);

        result.Should().NotBeNull();
        result!.Id.Should().Be(ToUuid("okko-95"));
    }

    [Fact]
    public void NoCrosswalkAtAll_BehavesExactlyAsBefore()
    {
        // The parameter is optional, so a caller that never learned about the table is unchanged —
        // including the null answer, which is what makes an import reject a row loudly rather than
        // guess a fuel for it.
        var result = OkkoFuelClassifier.ResolveFuelType(
            rawText: "OKKO\n20 л\n99999600000020368126",
            qrPayload: "9015$2000$;99999600000020368126=4507101299?",
            ProductionOkkoFuelTypes);

        result.Should().NotBeNull();
        result!.Id.Should().Be(ToUuid("okko-95"));
    }

    /// <summary>
    /// The names this catalogue creates must be names the classifier can read back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CanonicalName</c> is what the catalogue import names a fuel after, and
    /// <c>CategoryFromName</c> is how the voucher importer finds that fuel again. If the two do not
    /// agree, the system creates a fuel it cannot later resolve: "Пульс 95" read as a plain 95 and
    /// "Пропан" read as no grade at all, which is how a premium or a gas voucher came to be
    /// refused - the fuel was there, under a name the classifier did not recognise.
    /// </para>
    /// <para>
    /// The rule is now that every name the catalogue writes is read back as the grade it stands
    /// for, and the test says so over all five of them.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(OkkoFuelCategory.A95)]
    [InlineData(OkkoFuelCategory.A95Pulls)]
    [InlineData(OkkoFuelCategory.Diesel)]
    [InlineData(OkkoFuelCategory.DieselPulls)]
    [InlineData(OkkoFuelCategory.Gas)]
    public void EveryNameTheCatalogueWrites_IsReadBackAsItsGrade(OkkoFuelCategory grade)
    {
        var name = OkkoFuelClassifier.CanonicalName(grade);

        OkkoFuelClassifier.CategoryFromName(name).Should().Be(grade,
            $"'{name}' is the name this catalogue gives {grade}, and the voucher importer has to be "
            + "able to find that fuel by its name");
    }

    [Fact]
    public void TheFuelTheCatalogueCreates_ResolvesTheVoucherThatCarriesItsGrade()
    {
        // The whole point of the round trip: a catalogue built by the import - names and all - has
        // to resolve every voucher the brand publishes, by the name the import gave the fuel.
        var catalogue = new[]
        {
            new FuelTypeEntity { Id = ToUuid("c-a95"), Name = OkkoFuelClassifier.CanonicalName(OkkoFuelCategory.A95) },
            new FuelTypeEntity { Id = ToUuid("c-a95pulls"), Name = OkkoFuelClassifier.CanonicalName(OkkoFuelCategory.A95Pulls) },
            new FuelTypeEntity { Id = ToUuid("c-diesel"), Name = OkkoFuelClassifier.CanonicalName(OkkoFuelCategory.Diesel) },
            new FuelTypeEntity { Id = ToUuid("c-dieselpulls"), Name = OkkoFuelClassifier.CanonicalName(OkkoFuelCategory.DieselPulls) },
            new FuelTypeEntity { Id = ToUuid("c-gas"), Name = OkkoFuelClassifier.CanonicalName(OkkoFuelCategory.Gas) }
        };

        var cases = new (string Code, string Id)[]
        {
            ("9015", "c-a95"),
            ("45290", "c-dieselpulls"),
            ("9018", "c-diesel"),
            ("9019", "c-gas")
        };

        foreach (var (code, expected) in cases)
        {
            var resolved = OkkoFuelClassifier.ResolveFuelType(
                rawText: null,
                qrPayload: $"{code}$2000$;99999600000020368126=4507101299?",
                catalogue);

            resolved.Should().NotBeNull($"a voucher carrying the product code {code} names a grade the catalogue holds");
            resolved!.Id.Should().Be(ToUuid(expected),
                $"the voucher carrying {code} must land on the fuel the catalogue wrote for that grade");
        }
    }
}
