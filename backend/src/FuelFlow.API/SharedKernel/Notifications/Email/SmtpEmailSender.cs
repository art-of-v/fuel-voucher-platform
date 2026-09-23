using FuelFlow.SharedKernel.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FuelFlow.SharedKernel.Notifications.Email;

/// <summary>
/// Sends transactional email over the same SMTP account configured for support mail (the "SupportMail"
/// section), so no extra secrets are needed. Delivered as multipart/alternative: a branded HTML body
/// with the plain-text fallback retained, plus any inline images (the lion) as linked resources.
/// The visible From address stays the SMTP username (Gmail requires From to equal the authenticated
/// account); the display name and an optional Reply-To are configurable via SupportMailOptions.
/// </summary>
internal sealed class SmtpEmailSender : IEmailSender
{
    private readonly SupportMailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SupportMailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Host)
        && !string.IsNullOrWhiteSpace(_options.Username)
        && !string.IsNullOrWhiteSpace(_options.Password);

    public async Task SendAsync(string toEmail, EmailMessage message, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("SMTP is not configured; cannot send email.");

        var mime = BuildMimeMessage(toEmail, message);

        var secure = _options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, secure, cancellationToken);
        try
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
            await client.SendAsync(mime, cancellationToken);
        }
        finally
        {
            await client.DisconnectAsync(true, cancellationToken);
        }

        _logger.LogInformation("Email delivered to the configured recipient.");
    }

    /// <summary>Builds the MIME message — From/To/Reply-To/subject plus a multipart/alternative body
    /// (branded HTML + retained plain text) with any inline images added as linked resources referenced
    /// by <c>cid:</c>. Separated from the SMTP transport so the message shape is unit-testable without a
    /// live connection. From is always the authenticated <see cref="SupportMailOptions.Username"/>
    /// (Gmail requires it); only the display name and the optional Reply-To are configurable.</summary>
    internal MimeMessage BuildMimeMessage(string toEmail, EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.Username));
        mime.To.Add(new MailboxAddress(string.Empty, toEmail));
        if (!string.IsNullOrWhiteSpace(_options.ReplyToEmail))
            mime.ReplyTo.Add(new MailboxAddress(_options.FromName, _options.ReplyToEmail));
        mime.Subject = message.Subject;

        var builder = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
        };
        foreach (var image in message.InlineImages)
        {
            var parts = image.MediaType.Split('/', 2);
            var contentType = parts.Length == 2 ? new ContentType(parts[0], parts[1]) : new ContentType("image", "png");
            var resource = builder.LinkedResources.Add(image.ContentId, image.Content, contentType);
            resource.ContentId = image.ContentId;
            resource.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
        }
        mime.Body = builder.ToMessageBody();
        return mime;
    }
}
