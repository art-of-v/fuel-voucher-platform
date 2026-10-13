using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Feedback;
using FuelFlow.Features.Feedback.SharedModels;
using Xunit;

namespace FuelFlow.UnitTests.Feedback;

/// <summary>
/// Parsing is where the triage product decision is enforced. A parser that guesses is worse than
/// no triage: a confidently mislabelled message filters the admin view and mislabels the staff
/// group, while an unclassified one is simply read verbatim.
/// </summary>
public class GeminiFeedbackClassifierParseTests
{
    [Fact]
    public void AKeptComplaintKeepsItsCategorySeverityAndSummary()
    {
        var result = GeminiFeedbackClassifier.Parse("""
            {
              "verdict": "Kept",
              "category": "Complaint",
              "severity": "High",
              "summary": "Не працює оплата карткою.",
              "language": "Uk",
              "confidence": 0.93
            }
            """);

        result.Verdict.Should().Be(FeedbackVerdict.Kept);
        result.Category.Should().Be(FeedbackCategory.Complaint);
        result.Severity.Should().Be(FeedbackSeverity.High);
        result.Summary.Should().Be("Не працює оплата карткою.");
        result.Language.Should().Be(FeedbackLanguage.Uk);
        result.Confidence.Should().BeApproximately(0.93, 0.001);
    }

    [Fact]
    public void AGreetingIsJunkAndLosesEveryOptionalField()
    {
        // Even if the model helpfully categorised "йоук", the parser refuses to keep it: a
        // categorised greeting would skew the admin's category filters.
        var result = GeminiFeedbackClassifier.Parse("""
            {
              "verdict": "Junk",
              "category": "Ux",
              "severity": "High",
              "summary": "Привіт",
              "language": "Uk",
              "confidence": 0.99
            }
            """);

        result.Verdict.Should().Be(FeedbackVerdict.Junk);
        result.Category.Should().BeNull();
        result.Severity.Should().BeNull();
        result.Summary.Should().BeNull();
        result.Language.Should().Be(FeedbackLanguage.Uk);
    }

    [Fact]
    public void AKeptMessageWithNoCategoryFallsBackToOther()
    {
        var result = GeminiFeedbackClassifier.Parse("""
            { "verdict": "Kept", "severity": "null", "language": "Ru", "confidence": 0.7 }
            """);

        result.Category.Should().Be(FeedbackCategory.Other);
        result.Severity.Should().Be(FeedbackSeverity.Low);
        result.Summary.Should().BeNull();
    }

    [Fact]
    public void TheLiteralNullEnumMemberMeansAbsent()
    {
        var result = GeminiFeedbackClassifier.Parse("""
            { "verdict": "Kept", "category": "null", "severity": "null", "language": "En", "confidence": 0.8 }
            """);

        result.Category.Should().Be(FeedbackCategory.Other);
        result.Severity.Should().Be(FeedbackSeverity.Low);
    }

    [Fact]
    public void AMissingConfidenceIsTreatedAsUncertainRatherThanCertain()
    {
        var result = GeminiFeedbackClassifier.Parse("""
            { "verdict": "Kept", "language": "Uk" }
            """);

        result.Confidence.Should().Be(0.5);
    }

    [Theory]
    [InlineData(1.7, 1.0)]
    [InlineData(-0.3, 0.0)]
    public void ConfidenceIsClampedIntoRange(double raw, double expected)
    {
        var json = JsonSerializer.Serialize(new
        {
            verdict = "Kept",
            language = "Uk",
            confidence = raw
        });

        GeminiFeedbackClassifier.Parse(json).Confidence.Should().BeApproximately(expected, 0.001);
    }

    [Fact]
    public void AnUnknownCategoryIsAFailureRatherThanAQuietFallback()
    {
        var act = () => GeminiFeedbackClassifier.Parse("""
            { "verdict": "Kept", "category": "Rant", "language": "Uk", "confidence": 0.9 }
            """);

        act.Should().Throw<InvalidOperationException>().WithMessage("*category*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{ "language": "Uk", "confidence": 0.9 }""")]
    [InlineData("""{ "verdict": "Maybe", "confidence": 0.9 }""")]
    public void AResponseWithoutAUsableVerdictIsAFailure(string json)
    {
        var act = () => GeminiFeedbackClassifier.Parse(json);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnumMembersAreMatchedCaseInsensitively()
    {
        // Models do not reliably honour enum casing even when handed a schema.
        var result = GeminiFeedbackClassifier.Parse("""
            { "verdict": "kept", "category": "bug", "severity": "high", "language": "uk", "confidence": 0.8 }
            """);

        result.Verdict.Should().Be(FeedbackVerdict.Kept);
        result.Category.Should().Be(FeedbackCategory.Bug);
        result.Severity.Should().Be(FeedbackSeverity.High);
        result.Language.Should().Be(FeedbackLanguage.Uk);
    }

    [Fact]
    public void AnOverlongSummaryIsTruncatedToWhatTheColumnHolds()
    {
        var json = JsonSerializer.Serialize(new
        {
            verdict = "Kept",
            language = "Uk",
            confidence = 0.8,
            summary = new string('x', 900)
        });

        GeminiFeedbackClassifier.Parse(json).Summary.Should().HaveLength(300);
    }
}