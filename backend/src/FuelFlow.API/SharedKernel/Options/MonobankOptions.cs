namespace FuelFlow.SharedKernel.Options;

public sealed class MonobankOptions
{
    public const string SectionName = "Monobank";

    public string Token { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.monobank.ua";
    public string PublicKey { get; set; } = string.Empty;
    public bool Enabled { get; set; } = false;
    public string RedirectUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional map of key id → public key, used when Monobank signs webhooks with the
    /// X-Key-Id header (keys rotate). When X-Key-Id is absent, <see cref="PublicKey"/> is used.
    /// </summary>
    public Dictionary<string, string> PublicKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
