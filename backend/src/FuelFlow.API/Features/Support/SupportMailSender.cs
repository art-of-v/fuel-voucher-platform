using FuelFlow.Features.Support.SharedModels;
using FuelFlow.SharedKernel.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FuelFlow.Features.Support;

public interface ISupportMailSender
{
    /// <summary>Delivers a stored message. Throws on any failure; persistence
    /// and error capture are the caller's responsibility.
    /// A no-op when SMTP is not configured (message stays stored only).</summary>
    Task SendAsync(SupportMessage message, CancellationToken cancellationToken);

    /// <summary>True when SMTP credentials are present and SendAsync will
    /// actually attempt delivery. The handler consults this to decide whether
    /// a successful SendAsync may be recorded as EmailSentAtUtc.</summary>
    bool IsConfigured { get; }
}

/// <summary>
/// Sends support messages to the configured mailbox (palne.shopua@gmail.com in
/// production) via plain SMTP with STARTTLS. Gmail requires the From address to
/// be the authenticated account itself, so the visitor's address goes into
/// Reply-To instead — answering a message is then just hitting Reply.
/// </summary>
internal sealed class SupportMailSender : ISupportMailSender
{
    private readonly SupportMailOptions _options;
    private readonly ILogger<SupportMailSender> _logger;

    public SupportMailSender(IOptions<SupportMailOptions> options, ILogger<SupportMailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task SendAsync(SupportMessage message, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogWarning("SupportMail is not configured; message {Id} is stored but will not be emailed.", message.Id);
            return;
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.Username));
        mime.To.Add(new MailboxAddress(_options.FromName, _options.ToEmail));
        mime.ReplyTo.Add(new MailboxAddress(message.Email, message.Email));
        mime.Subject = $"Palne.shop — повідомлення від {message.Email}";

        var details = string.Join("\n",
            $"Email: {message.Email}",
            $"Час (UTC): {message.CreatedAtUtc:yyyy-MM-dd HH:mm:ss}",
            $"IP: {message.IpAddress ?? "—"}",
            $"User-Agent: {message.UserAgent ?? "—"}");

        mime.Body = new TextPart("plain")
        {
            Text = $"{message.Message}\n\n—\n{details}"
        };

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

        _logger.LogInformation("Support message {Id} emailed to {To}.", message.Id, _options.ToEmail);
    }
}
