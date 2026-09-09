namespace FuelFlow.Features.Support.SharedModels;

/// <summary>A message submitted through the public /support page on palne.shop.
/// Persisted before any delivery attempt so a SMTP outage never loses a
/// customer message; EmailSentAtUtc stays null until the mail actually went out.</summary>
public sealed class SupportMessage
{
    public Guid Id { get; set; }

    /// <summary>Reply address of the person who wrote in (validated at the edge).</summary>
    public string Email { get; set; } = null!;

    public string Message { get; set; } = null!;

    /// <summary>Free-form browser User-Agent, truncated. Diagnostic only.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Client IP as seen by the API (after forwarded-header processing).
    /// Used to spot abuse; never shown anywhere.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Null until the message was successfully handed to the SMTP server.
    /// Null is normal when SupportMail is not configured (e.g. dev).</summary>
    public DateTime? EmailSentAtUtc { get; set; }

    /// <summary>Last SMTP failure reason, truncated. Null when delivery succeeded
    /// or was never attempted.</summary>
    public string? SendError { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
