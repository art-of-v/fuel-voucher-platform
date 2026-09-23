namespace FuelFlow.SharedKernel.Notifications.Email;

/// <summary>
/// A transactional email in both of its required forms: a branded <see cref="HtmlBody"/> and the
/// retained plain-text <see cref="TextBody"/> fallback (sent together as multipart/alternative, so a
/// client that cannot render HTML — or a spam filter — still sees the full message). Any images the
/// HTML references by <c>cid:</c> travel in <see cref="InlineImages"/> so they embed inline and do
/// not trip the recipient's "show images" prompt.
/// </summary>
public sealed record EmailMessage
{
    public required string Subject { get; init; }

    /// <summary>Branded, inline-styled, email-client-safe HTML.</summary>
    public required string HtmlBody { get; init; }

    /// <summary>Plain-text alternative. Never omitted — accessibility and spam-score both depend on it.</summary>
    public required string TextBody { get; init; }

    /// <summary>Images referenced from <see cref="HtmlBody"/> by <c>cid:</c>, embedded as linked
    /// resources. Empty for a text-only-styled message.</summary>
    public IReadOnlyList<EmailInlineImage> InlineImages { get; init; } = [];
}

/// <summary>
/// An image embedded in an email and referenced from the HTML by <c>cid:{ContentId}</c>. Carried as
/// bytes so the transport (MimeKit) owns nothing brand-specific.
/// </summary>
/// <param name="ContentId">The Content-Id the HTML references (without angle brackets).</param>
/// <param name="MediaType">MIME type, e.g. <c>image/png</c>.</param>
/// <param name="Content">Raw image bytes.</param>
public sealed record EmailInlineImage(string ContentId, string MediaType, byte[] Content);
