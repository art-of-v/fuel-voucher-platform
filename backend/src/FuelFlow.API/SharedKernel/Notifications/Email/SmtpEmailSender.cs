using FuelFlow.SharedKernel.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FuelFlow.SharedKernel.Notifications.Email;

/// <summary>
/// Sends transactional email over the same SMTP account configured for support mail
/// (the "SupportMail" section), so no extra secrets are needed. Gmail requires the
/// From address to equal the authenticated account, so From is always the SMTP
/// username. IsConfigured intentionally ignores ToEmail, which is support-only.
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

    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("SMTP is not configured; cannot send email.");

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.Username));
        mime.To.Add(new MailboxAddress(string.Empty, toEmail));
        mime.Subject = subject;
        mime.Body = new TextPart("plain") { Text = body };

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

        _logger.LogInformation("Email delivered to the configured recipient (subject: {Subject}).", subject);
    }
}
