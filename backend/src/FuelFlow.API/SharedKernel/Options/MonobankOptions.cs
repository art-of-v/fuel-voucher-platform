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
    /// Safety net for lost or late payment webhooks. When enabled, a recurring job polls
    /// Monobank's invoice-status API for orders still awaiting payment and drives them
    /// through the same transition path a webhook would. Only runs when <see cref="Enabled"/>
    /// is also true (i.e. against the real Monobank client, never the in-process mock, whose
    /// unknown-invoice fallback is a fabricated "success").
    /// </summary>
    public bool ReconciliationEnabled { get; set; } = true;

    /// <summary>Grace period before an unpaid order is polled, so the webhook is given time to arrive first.</summary>
    public int ReconciliationMinAgeMinutes { get; set; } = 3;

    /// <summary>Orders older than this are no longer polled; their Monobank invoices have long since expired.</summary>
    public int ReconciliationMaxAgeHours { get; set; } = 24;

    /// <summary>Maximum orders reconciled per run, bounding the Monobank status calls per cycle.</summary>
    public int ReconciliationBatchSize { get; set; } = 100;

    /// <summary>
    /// Optional map of key id → public key, used when Monobank signs webhooks with the
    /// X-Key-Id header (keys rotate). When X-Key-Id is absent, <see cref="PublicKey"/> is used.
    /// </summary>
    public Dictionary<string, string> PublicKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
