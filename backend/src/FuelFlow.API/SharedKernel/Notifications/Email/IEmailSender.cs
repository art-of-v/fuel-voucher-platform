namespace FuelFlow.SharedKernel.Notifications.Email;

/// <summary>
/// Minimal outbound email abstraction shared by admin OTP delivery and any future
/// transactional mail. Kept separate from ISupportMailSender, which is bound to the
/// contact-form message format.
/// </summary>
public interface IEmailSender
{
    /// <summary>True when SMTP credentials are present and SendAsync will actually
    /// attempt delivery. Callers fall back to another channel when this is false.</summary>
    bool IsConfigured { get; }

    /// <summary>Delivers a plain-text email. Throws on any failure - including no SMTP
    /// configured - so the caller can decide the fallback. Callers should check
    /// IsConfigured first.</summary>
    Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken);
}
