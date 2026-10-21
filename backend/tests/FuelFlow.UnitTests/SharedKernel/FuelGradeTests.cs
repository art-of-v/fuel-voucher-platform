using FluentAssertions;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.SharedKernel.Domain;
using Xunit;

namespace FuelFlow.UnitTests.SharedKernel;

/// <summary>
/// The grade key: the rule that says "А-95" and "A-95" are one grade.
/// </summary>
/// <remarks>
/// <para>
/// The owner's decision (planning #349, 2026-10-21): "А-95" and "A-95" are the same grade, and the
/// catalogue should say so once. The key is how it says it - the name folded onto one spelling, so
/// that the second spelling does not become a second row.
/// </para>
/// <para>
/// The database applies the same rule (<c>fuelflow_fuel_grade_key(name)</c>, in
/// <c>fuel_types.grade_key</c>), and the two are compared over this corpus by
/// <c>FuelTypeGradeKeyTests</c> in the integration suite - a constraint that computes a different
/// key from the one the application believes in would refuse a fuel the application believes is a
/// different grade.
/// </para>
/// </remarks>
public sealed class FuelGradeTests
{
    /// <summary>
    /// The names the catalogue actually holds, and the spellings of them that a person or a brand
    /// might use. Production's 23 fuels and the seeded 10, read off the live database on
    /// 2026-10-21, plus the variants that are one grade in two spellings.
    /// </summary>
    public static readonly string[] TheCatalogue = 
    [
        // Production, KLO.
        "А 92", "А 95", "А 95+", "Газ", "ДП",
        // Production, OKKO.
        "А-95", "Пропан", "Пульс 95", "Пульс ДП",
        // Production, UPG.
        "A-95", "EURO DIESEL", "upg100", "upg95", "upgDIESEL",
        // Production, WOG.
        "100 Mustang Євро5-Е0", "95 Євро5-Е10", "95 Mustang Євро5-Е10", "ГАЗ",
        "ДП Євро5", "ДП Mustang+", "AdBlue",
        // The seeded rows a freshly migrated database holds.
        "ДП ЄВРО", "ДП PULLS", "A 95 EURO", "A-95 Mustang", "Mustang 100", "Pulls 95"
    ];

    [Theory]
    [InlineData("А-95", "A-95")]
    [InlineData("А-95", "а-95")]
    [InlineData("А-95", "А95")]
    [InlineData("А-95", "А 95")]
    [InlineData("А-95", "а 95")]
    [InlineData("ДП", "DP")]
    [InlineData("ДП", "дп")]
    [InlineData("Пульс 95", "Pulls 95")]
    [InlineData("Пульс 95", "пульс95")]
    [InlineData("Пропан", "propan")]
    [InlineData("Газ", "ГАЗ")]
    [InlineData("А95 ЄВРО", "A-95 EURO")]
    [InlineData("ДП Євро5", "ДП Euro 5")]
    [InlineData("ДП Mustang+", "дп mustang+")]
    public void OneGrade_TwoSpellings_AreOneKey(string left, string right)
    {
        // The owner's case, and every spelling of it: one grade, one key, whatever language and
        // whatever punctuation it is written in.
        FuelGrade.SameGrade(left, right).Should().BeTrue(
            "'{left}' and '{right}' are one grade, so the catalogue says one row for them");
    }

    [Theory]
    [InlineData("А 95", "А 95+")]
    [InlineData("А 92", "А 95")]
    [InlineData("ДП", "ДП PULLS")]
    [InlineData("ДП", "Пульс ДП")]
    [InlineData("upg95", "A-95")]
    [InlineData("upgDIESEL", "EURO DIESEL")]
    [InlineData("upg95", "upg100")]
    [InlineData("А-95", "А 92")]
    [InlineData("95 Євро5-Е10", "95 Mustang Євро5-Е10")]
    public void TwoGrades_AreTwoKeys(string left, string right)
    {
        // The dangerous direction: a key that merged two grades would refuse a fuel the network
        // really sells, and the refusal would name nothing.
        FuelGrade.SameGrade(left, right).Should().BeFalse(
            "'{left}' and '{right}' are two grades, and a rule that merged them would refuse a real fuel");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    [InlineData("...")]
    public void ANameThatSaysNoGrade_HasNoKey(string? name)
    {
        // A name of punctuation says no grade, and a row with no key is excluded from the
        // database's index - the same shape as a fuel with no brand code.
        FuelGrade.Key(name).Should().BeNull();
        FuelGrade.SameGrade(name, "А-95").Should().BeFalse(
            "a name that says no grade is not the grade we already sell");
    }

    [Fact]
    public void TheKey_IsTheSame_WhateverTheCase()
    {
        foreach (var name in TheCatalogue)
        {
            FuelGrade.Key(name.ToUpperInvariant()).Should().Be(FuelGrade.Key(name),
                "a name is written in whatever case the person who typed it used");
            FuelGrade.Key(name.ToLowerInvariant()).Should().Be(FuelGrade.Key(name));
        }
    }

    [Fact]
    public void EveryNameTheCatalogueHolds_SaysSomeGradeOrNone()
    {
        // Nothing in the catalogue folds to nothing by accident: a name that says no grade is a
        // deliberate thing (a rehearsal name, a punctuation-only name), not a broken fold.
        foreach (var name in TheCatalogue)
        {
            var key = FuelGrade.Key(name);
            key.Should().NotBeNullOrWhiteSpace("'{name}' is a grade name");
            key!.Should().MatchRegex("^[a-z0-9+]+$",
                "a key is letters, digits and the plus - the characters that say what the grade is");
        }
    }

    /// <summary>
    /// The boundary of the rule, stated so nobody widens it by accident.
    /// </summary>
    /// <remarks>
    /// The key says the NAME, and the brand's own code says the PRODUCT. "Газ" and "LPG" are the
    /// same fuel and two names, and the key keeps them apart - because the key is what says "the
    /// same name", and only the brand's vocabulary can say "the same fuel". The catalogue import
    /// resolves by the grade the classifier reads, and the database refuses two rows claiming one
    /// brand code, so the product is identified twice over: once by the code the brand publishes,
    /// and once by the spelling of the name we hold.
    /// </remarks>
    [Fact]
    public void TheKey_SaysTheName_AndTheCode_SaysTheProduct()
    {
        FuelGrade.SameGrade("Газ", "LPG").Should().BeFalse(
            "the key says the name, and only the brand's code can say the product");
        FuelGrade.SameGrade("Пропан", "ГАЗ").Should().BeFalse(
            "and the same: 'Пропан' is the name this catalogue gives the gas grade, and 'ГАЗ' is "
            + "another name for it - the code is what says they are one fuel");

        // What the classifier reads is a coarser question, and it merges more than the key does:
        // both spellings are the gas grade to it.
        OkkoFuelClassifier.CategoryFromName("Газ").Should().Be(OkkoFuelClassifier.CategoryFromName("LPG"));
    }

    /// <summary>
    /// The constraint and the classifier must agree - the issue's own words. They agree in this
    /// direction: two names with one key are one grade, so they must land in one category, or the
    /// database would refuse a row the classifier calls the same fuel.
    /// </summary>
    [Fact]
    public void TheClassifier_AgreesWithTheKey()
    {
        foreach (var left in TheCatalogue)
        {
            foreach (var right in TheCatalogue)
            {
                if (!FuelGrade.SameGrade(left, right)) continue;

                OkkoFuelClassifier.CategoryFromName(left)
                    .Should().Be(OkkoFuelClassifier.CategoryFromName(right),
                        "'{left}' and '{right}' are one grade, so they are one category - the "
                        + "classifier and the database's index have to say the same thing");
            }
        }
    }
}
