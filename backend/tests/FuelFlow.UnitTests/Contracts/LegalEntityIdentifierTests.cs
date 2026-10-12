using FluentAssertions;
using FuelFlow.Features.Contracts.SharedModels;
using Xunit;

namespace FuelFlow.UnitTests.Contracts;

/// <summary>
/// The legal-entity identifier rule, in two layers. #128 pinned the format gate after a company was
/// created with EDRPOU <c>1234567890456565</c> - 16 digits, accepted because the only check was "not
/// empty". #266 added the control digit, because the format gate cannot see a transposed digit and
/// <c>legal_entities.edrpou</c> is globally unique, which makes a typo permanent.
/// </summary>
public sealed class LegalEntityIdentifierTests
{
    // The value from the #128 issue, verbatim.
    private const string ObservedGarbage = "1234567890456565";

    /// <summary>
    /// Registered Ukrainian entities whose ЕДРПОУ was taken from a published source and checked
    /// against the control-digit rule. These are the evidence the algorithm is right: a checksum
    /// nobody has ever seen a real value pass is not a checksum.
    /// </summary>
    /// <remarks>
    /// <para>Provenance, in order:</para>
    /// <list type="bullet">
    /// <item><description>
    /// PAT «Укрнафта» and its 22 structural subdivisions, published as a table on the company's
    /// Ukrainian Wikipedia article (uk.wikipedia.org/wiki/Укрнафта, revision 48947299).
    /// </description></item>
    /// <item><description>
    /// The three worked examples carried by the published descriptions of the algorithm: a Ukrainian
    /// banking textbook on error-detecting codes, a 1C forum article, and a 2003 Excel-methodology
    /// paper. <c>38528147</c> is the sample the #266 issue could not reproduce.
    /// </description></item>
    /// <item><description>
    /// Entities carrying a "Код ЄДРПОУ" field in their Ukrainian Wikipedia articles, collected via the
    /// MediaWiki API (insource search) to reach codes beyond one company's subsidiary list.
    /// </description></item>
    /// </list>
    /// <para>
    /// The set deliberately spans both weight sequences: 36 codes take the ordinal weights and 47 take
    /// the rotated ones. 43 of them - including every code above 30 000 000 - have a seven-digit prefix
    /// below 30 000 000, so they are accepted only when the weights are chosen by the <em>whole
    /// eight-digit code</em>. Reading the prefix instead is the one way to get this algorithm wrong,
    /// and it fails loudly here rather than silently in production.
    /// </para>
    /// <para>
    /// Four of them exercise the second pass, where a remainder of 10 forces the shifted weights:
    /// <c>00136875</c>, <c>25688243</c> and <c>04363299</c> (ordinal), <c>36959159</c> (rotated).
    /// </para>
    /// </remarks>
    private static readonly string[] RegisteredCodes =
    [
        // Ukrnafta group.
        "00135390", "00136490", "00136515", "00136573", "00136538", "20564147", "00135415",
        "00136875", "00143113", "00142800", "00136857", "26429551", "00147140", "00137035",
        "00142875", "00143455", "05414835", "05398533", "00136544", "00137041", "22525915",
        "33603711", "33948480",

        // The published worked examples, including the #266 sample that would not reproduce.
        "38528147", "04903012", "32855961",

        // Company articles carrying their own registry code.
        "20077720", "39307323", "00178353", "37014600", "31366910", "34063592", "36028628",
        "40075815", "35379792", "32208748", "35076450", "25688243", "37507880", "44773264",
        "31028309", "26221922", "33549199", "04057907", "21510633", "36328309", "39204556",
        "34074667", "43459479", "45034197", "45352426", "43818534", "42718133", "40938062",
        "38747289", "00191158", "14307529", "30800690", "04363299", "34217974", "03753680",
        "25300393", "36959159", "43375001", "41988051", "30822761", "37535703", "31831604",
        "32598402", "30010348", "04359399", "34199416", "45549108", "41243196", "45362298",
        "41875915", "23729809", "39071613", "02473895", "24172993", "22275298", "30019775",
        "04362674",
    ];

    [Theory]
    [InlineData("3852814")]           // one short
    [InlineData("385281470")]         // one long
    public void Rejects_Edrpou_ThatIsNotEightDigits(string edrpou)
        => LegalEntityIdentifier.DescribeEdrpouProblem(edrpou).Should().NotBeNull();

    [Theory]
    [InlineData(ObservedGarbage)]    // 16 digits - the defect in #128
    [InlineData("3852814 ")]
    [InlineData("3852814a")]          // a letter in the last position
    [InlineData("38528 47")]          // a space inside the number
    [InlineData("385-28147")]
    [InlineData("3.8528147")]
    [InlineData("abcdefgh")]
    [InlineData("３８５２８１４７")]      // fullwidth digits: 8 characters, not 8 ASCII digits
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejects_Malformed_Edrpou(string? edrpou)
        => LegalEntityIdentifier.DescribeEdrpouProblem(edrpou).Should().NotBeNull();

    [Theory]
    [InlineData("38528147")]
    [InlineData("00000000")]
    [InlineData(" 38528147 ")]        // surrounding whitespace is what a paste adds; tolerate it
    public void Accepts_EightDigit_Edrpou(string edrpou)
        => LegalEntityIdentifier.DescribeEdrpouProblem(edrpou).Should().BeNull();

    [Fact]
    public void Accepts_EveryRegisteredCode_TheCorpusWasBuiltFrom()
    {
        // The decisive test. Every one of these is a code the registry actually issued.
        var rejected = RegisteredCodes
            .Where(code => LegalEntityIdentifier.DescribeEdrpouProblem(code) != null)
            .ToList();

        rejected.Should().BeEmpty("the control-digit rule must accept every real registered code");
    }

    [Fact]
    public void RegisteredCode_Corpus_CoversBothWeightSequences()
    {
        // Guards the corpus itself. If somebody trims it down to whichever codes their implementation
        // happens to pass, the half of the registry using the other sequence stops being tested and
        // the trim is invisible.
        var belowRotation = RegisteredCodes.Count(code => long.Parse(code) < 30_000_000);
        var inRotation = RegisteredCodes.Count(code => long.Parse(code) is >= 30_000_000 and <= 60_000_000);

        belowRotation.Should().BeGreaterThan(0, "the ordinal weight sequence needs coverage");
        inRotation.Should().BeGreaterThan(0, "the rotated weight sequence needs coverage");
    }

    [Fact]
    public void Chooses_TheWeightSequence_ByTheWholeCode_NotByThePrefix()
    {
        // 38528147 has a seven-digit prefix of 3852814, which is under 30 000 000 and would select the
        // ordinal weights. The whole code is over it, and the registry rotated the weights. This is
        // the exact sample #266 could not reproduce; the issue blamed the sample, but the sample was
        // right all along.
        LegalEntityIdentifier.IsValidEdrpou("38528147").Should().BeTrue();

        // 33603711 is a second company from the same table, and the same trap.
        LegalEntityIdentifier.IsValidEdrpou("33603711").Should().BeTrue();
    }

    [Theory]
    [InlineData("38528148")]   // correct digits, last one bumped
    [InlineData("38528141")]   // correct digits, last one lowered
    [InlineData("38528174")]   // the last two digits swapped - the typo a checksum exists to catch
    [InlineData("38258147")]   // a transposition in the middle
    [InlineData("83528147")]   // a transposition at the front
    [InlineData("10000002")]   // what the integration fixtures used to say: 8 digits, wrong check
    public void Rejects_Edrpou_WhoseControlDigitIsWrong(string edrpou)
        => LegalEntityIdentifier.DescribeEdrpouProblem(edrpou).Should().NotBeNull();

    [Fact]
    public void Catches_AlmostEveryTransposedDigit()
    {
        // Why this layer earns its place on top of the format gate: a transposition is 8 digits long
        // and looks perfectly well-formed, so #128's rule waved it straight through.
        var total = 0;
        var caught = 0;

        foreach (var code in RegisteredCodes)
        {
            for (var position = 0; position < 7; position++)
            {
                if (code[position] == code[position + 1])
                {
                    continue;
                }

                var characters = code.ToCharArray();
                (characters[position], characters[position + 1]) =
                    (characters[position + 1], characters[position]);
                var transposed = new string(characters);

                total++;
                if (LegalEntityIdentifier.IsValidEdrpou(transposed))
                {
                    continue;
                }

                caught++;
            }
        }

        total.Should().BeGreaterThan(0);
        caught.Should().BeGreaterThan((int)(total * 0.8),
            "a modulo-11 check catches roughly 9 of every 10 single-digit errors");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Accepts_Absent_Vat_Because_Vat_Is_Optional(string? vat)
        => LegalEntityIdentifier.DescribeVatProblem(vat).Should().BeNull();

    [Theory]
    [InlineData("123456789012")]      // 12 digits
    [InlineData(" 123456789012 ")]
    public void Accepts_TwelveDigit_Vat(string vat)
        => LegalEntityIdentifier.DescribeVatProblem(vat).Should().BeNull();

    [Theory]
    [InlineData("12345678901")]       // 11 digits
    [InlineData("1234567890123")]     // 13 digits
    [InlineData("12345678901x")]
    [InlineData("not-a-vat")]
    public void Rejects_Present_But_Malformed_Vat(string vat)
    {
        // A present-but-wrong VAT is reported rather than ignored: the operator who typed it should
        // hear that it is wrong instead of having it quietly dropped.
        LegalEntityIdentifier.DescribeVatProblem(vat).Should().NotBeNull();
    }

    [Fact]
    public void ErrorMessages_Say_TheExpectedLength()
    {
        // The operator has to be able to fix the field from the message alone.
        LegalEntityIdentifier.DescribeEdrpouProblem(ObservedGarbage)
            .Should().Be($"EDRPOU must be exactly {LegalEntityIdentifier.EdrpouLength} digits");

        LegalEntityIdentifier.DescribeVatProblem("123")
            .Should().Be($"VAT must be exactly {LegalEntityIdentifier.VatLength} digits");
    }

    [Fact]
    public void ErrorMessage_DistinguishesAWrongControlDigit_FromTheWrongLength()
    {
        // "Must be exactly 8 digits" would be useless to somebody whose eight digits are all correct.
        // The two failures need different advice, so they get different sentences.
        var problem = LegalEntityIdentifier.DescribeEdrpouProblem("38528174");

        problem.Should().NotBeNull();
        problem.Should().NotContain($"exactly {LegalEntityIdentifier.EdrpouLength}",
            "the length is right here - reporting the length would send the operator to fix the wrong thing");
        problem.Should().Contain("control digit");
    }
}