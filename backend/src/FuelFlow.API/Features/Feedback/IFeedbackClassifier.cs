using FuelFlow.Features.Feedback.SharedModels;

namespace FuelFlow.Features.Feedback;

/// <summary>The triage outcome for one customer message.</summary>
/// <param name="Verdict">Whether the message carries product information at all.</param>
/// <param name="Category">What a kept message is about; null when <paramref name="Verdict"/> is Junk.</param>
/// <param name="Severity">How much a kept message should pull at the roadmap.</param>
/// <param name="Summary">One sentence restating the message, for the staff group.</param>
/// <param name="Language">Language the customer wrote in.</param>
/// <param name="Confidence">Classifier certainty, 0..1.</param>
public sealed record FeedbackClassification(
    FeedbackVerdict Verdict,
    FeedbackCategory? Category,
    FeedbackSeverity? Severity,
    string? Summary,
    FeedbackLanguage Language,
    double Confidence);

/// <summary>
/// Decides whether a message is real product feedback or noise.
/// <para>
/// An interface rather than a concrete Gemini call because the brand is the most likely thing to
/// change (quota, price, or a different model that holds Ukrainian better) and because the failure
/// mode that matters is availability, not cleverness: when the classifier is unreachable the
/// message is still stored and still readable by a human.
/// </para>
/// </summary>
public interface IFeedbackClassifier
{
    /// <summary>Whether a classifier is configured at all. When false the pipeline stores the row
    /// unclassified and the team reads it verbatim.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Classifies one message. Throws on transport or brand failure - the caller records the
    /// error on the row and leaves the verdict null.
    /// </summary>
    Task<FeedbackClassification> ClassifyAsync(
        string text,
        CancellationToken ct = default);
}