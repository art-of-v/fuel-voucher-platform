namespace FuelFlow.SharedKernel.Options;

public sealed class SupportMailOptions
{
    public const string SectionName = "SupportMail";

    /// <summary>SMTP host, e.g. smtp.gmail.com. Empty = mail delivery is disabled;
    /// messages are still persisted to the database.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>587 = STARTTLS (Gmail default), 465 = implicit TLS.</summary>
    public int Port { get; set; } = 587;

    /// <summary>SMTP login. With Gmail this must be the account's own address,
    /// and the password must be a 16-character App Password (plain account
    /// passwords are rejected by Google).</summary>
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Where contact-form messages are delivered, e.g. palne.shopua@gmail.com.
    /// Defaults to Username when empty.</summary>
    public string ToEmail { get; set; } = string.Empty;

    /// <summary>Sender display name. Defaults to "Palne.shop".</summary>
    public string FromName { get; set; } = "Palne.shop";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password)
        && !string.IsNullOrWhiteSpace(ToEmail);
}
