using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using FuelFlow.Features.Feedback.SharedModels;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Feedback;

/// <summary>Brand settings for the triage step. Separate from Telegram's own configuration
/// because the two services fail, expire and get rotated independently.</summary>
public sealed class FeedbackClassifierOptions
{
    public const string SectionName = "Llm";

    /// <summary>Leave off to run the feedback pipeline with no classifier at all: messages are
    /// stored verbatim and every one of them is treated as worth reading.</summary>
    public bool Enabled { get; set; } = false;

    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    public string Model { get; set; } = "gemini-2.0-flash";

    /// <summary>The deadline the model is given. A triage step that waits a hundred seconds is a
    /// feedback message nobody reads.</summary>
    public const int DefaultTimeoutSeconds = 20;

    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);
}

/// <summary>
/// Classifies feedback with Gemini.
/// <para>
/// The response is requested as JSON with a fixed schema and parsed strictly: a brand that
/// answers with prose, a fenced code block, or an unknown category is treated as a failure rather
/// than being coerced into something plausible, because a confidently wrong category is worse than
/// an unclassified message - it filters the admin view and the staff group's labels.
/// </para>
/// <para>
/// Prompt wording is pinned rather than improvised: the verdict boundary ("carries information
/// about the app") is the whole product decision here, and a prompt that drifts between runs makes
/// the junk rate unknowable.
/// </para>
/// </summary>
public sealed class GeminiFeedbackClassifier : IFeedbackClassifier
{
    private const string SystemInstruction =
        """
        You triage messages that customers sent to a fuel-voucher mobile app's support bot.
        Decide whether a message carries information about the app.

        verdict = "Junk" when the message says nothing about the app: greetings and farewells
        ("привіт", "йоук", "добрий день", "hi"), thanks without a reason, laughter, emoji-only
        messages, repeated filler, advertising, or messages addressed to a person rather than
        to the product.

        verdict = "Kept" when the message reports something about the app: a complaint, a bug,
        a missing feature, a question about how it works, pricing, or genuine praise.

        category: Bug (something is broken), Idea (a request for something new), Complaint
        (dissatisfaction without a specific fault), Ux (the flow works but is confusing),
        Question (asking how something works), Praise (positive feedback), Other.

        severity: High (cannot pay, cannot get fuel, lost money, cannot log in), Medium (a real
        problem with a workaround), Low (cosmetic or a mild preference).

        summary: restate the customer's message in ONE sentence, in Ukrainian, at most 120
        characters. Do not add facts, causes or conclusions that the customer did not state.
        Leave it null for Junk.

        language: Uk, Ru, En or Other.

        confidence: your certainty that the verdict is right, between 0 and 1. Be low rather
        than high when a message is genuinely ambiguous.
        """;

    private static readonly string ResponseSchema =
        """
        {
          "type": "OBJECT",
          "properties": {
            "verdict":    { "type": "STRING", "enum": ["Junk", "Kept"] },
            "category":   { "type": "STRING", "enum": ["Bug", "Idea", "Complaint", "Ux", "Question", "Praise", "Other", "null"] },
            "severity":   { "type": "STRING", "enum": ["Low", "Medium", "High", "null"] },
            "summary":    { "type": "STRING" },
            "language":   { "type": "STRING", "enum": ["Uk", "Ru", "En", "Other"] },
            "confidence": { "type": "NUMBER" }
          },
          "required": ["verdict", "language", "confidence"]
        }
        """;

    private readonly HttpClient _http;
    private readonly FeedbackClassifierOptions _options;
    private readonly ILogger<GeminiFeedbackClassifier> _logger;

    public GeminiFeedbackClassifier(
        HttpClient http,
        IOptions<FeedbackClassifierOptions> options,
        ILogger<GeminiFeedbackClassifier> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<FeedbackClassification> ClassifyAsync(string text, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("The feedback classifier is not configured.");
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Nothing to classify.", nameof(text));

        var requestUri = $"{_options.BaseUrl.TrimEnd('/')}/models/{_options.Model}:generateContent";

        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = SystemInstruction } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = text } } }
            },
            generationConfig = new
            {
                temperature = 0,
                responseMimeType = "application/json",
                responseSchema = JsonSerializer.Deserialize<JsonElement>(ResponseSchema)
            }
        };

        using var response = await _http.PostAsJsonAsync(
            requestUri,
            payload,
            // The API key goes in a query parameter, not a header: that is how the Google AI Studio
            // endpoint authenticates. It never appears in a log line here because the URI is not logged.
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Classifier request failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase} — {error}");
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        var classification = Parse(await ExtractCandidateJsonAsync(body));
        _logger.LogInformation(
            "Classified a feedback message as {Verdict} ({Category}, confidence {Confidence}).",
            classification.Verdict, classification.Category, classification.Confidence);

        return classification;
    }

    /// <summary>
    /// Pulls the candidate's text out of the Gemini envelope. The schema guarantees JSON but the
    /// transport nests it under candidates[0].content.parts[*].text, and a refusal or a safety
    /// block has no text at all - both are failures here, not empty classifications.
    /// </summary>
    private static async Task<string> ExtractCandidateJsonAsync(string body)
    {
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Classifier response carried no candidates.");
        }

        var candidate = candidates[0];
        if (candidate.TryGetProperty("finishReason", out var reason)
            && reason.GetString() is { } finish
            && !string.Equals(finish, "STOP", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Classifier stopped with finishReason '{finish}'.");
        }

        if (!candidate.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Classifier response carried no content parts.");
        }

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var value) && !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString()!;
        }

        throw new InvalidOperationException("Classifier response contained no text.");
    }

    /// <summary>
    /// Strict JSON parse. An unknown enum member is an error rather than a fallback, so a model
    /// change that invents a category is visible in <c>classifier_error</c> instead of quietly
    /// landing in "Other".
    /// </summary>
    internal static FeedbackClassification Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            // A brand that answers with prose instead of the schema it was handed is a failure,
            // not an empty classification: silently defaulting here is how a greeting ends up in
            // the staff group labelled as a complaint.
            throw new InvalidOperationException("Classifier response was not JSON.", ex);
        }

        using (document)
        {
            return ParseDocument(document.RootElement);
        }
    }

    private static FeedbackClassification ParseDocument(JsonElement root)
    {

        var verdict = ReadEnum<FeedbackVerdict>(root, "verdict")
            ?? throw new InvalidOperationException("Classifier response has no usable verdict.");

        var confidence = root.TryGetProperty("confidence", out var confidenceElement)
                         && confidenceElement.ValueKind == JsonValueKind.Number
            ? Math.Clamp(confidenceElement.GetDouble(), 0, 1)
            : 0.5;

        var language = ReadEnum<FeedbackLanguage>(root, "language") ?? FeedbackLanguage.Other;

        if (verdict == FeedbackVerdict.Junk)
        {
            // A dropped message carries no category, no severity and no summary. Forcing nulls here
            // means a model that helpfully categorised "йоук" as Ux cannot skew the admin filters.
            return new FeedbackClassification(FeedbackVerdict.Junk, null, null, null, language, confidence);
        }

        var summary = root.TryGetProperty("summary", out var summaryElement)
                      && summaryElement.ValueKind == JsonValueKind.String
            ? Truncate(summaryElement.GetString(), 300)
            : null;

        return new FeedbackClassification(
            FeedbackVerdict.Kept,
            ReadEnum<FeedbackCategory>(root, "category") ?? FeedbackCategory.Other,
            ReadEnum<FeedbackSeverity>(root, "severity") ?? FeedbackSeverity.Low,
            summary,
            language,
            confidence);
    }

    private static T? ReadEnum<T>(JsonElement root, string property) where T : struct, Enum
    {
        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String)
            return null;

        var raw = element.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // The schema offers "null" as a literal for the optional members; that is absence, not a value.
        if (string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!Enum.TryParse<T>(raw, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Classifier returned unknown {property} '{raw}'."));
        }

        return parsed;
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? value : value[..Math.Min(value.Length, maxLength)];
}