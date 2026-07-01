namespace FuelFlow.Options;

public sealed class MonobankOptions
{
    public const string SectionName = "Monobank";

    public string Token { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.monobank.ua";
    public string PublicKey { get; set; } = string.Empty;
    public bool Enabled { get; set; } = false;
    public string RedirectUrl { get; set; } = string.Empty;
}
