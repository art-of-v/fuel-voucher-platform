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
    /// Sandbox merchant token, used only for QA accounts. Separate merchant profile, separate
    /// webhook-signing key, separate (nonexistent) settlement.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT defaulted to <see cref="Token"/>. If a QA account's routing resolves to
    /// the sandbox and this is empty, the correct behaviour is to refuse the checkout - not to
    /// quietly send the customer to the live merchant, which would take real money from someone
    /// who is testing. See <c>MonobankMerchantResolver</c>, which fails closed on that case.
    /// </remarks>
    public string SandboxToken { get; set; } = string.Empty;

    /// <summary>Webhook-signing public key belonging to the sandbox merchant.</summary>
    public string SandboxPublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Server-side allowlist of QA accounts, as full E.164 phone numbers.
    /// </summary>
    /// <remarks>
    /// The primary authority for which merchant a checkout may touch. It lives in deploy
    /// configuration rather than in a user-profile flag so that it cannot be edited from the
    /// admin panel or the API by anyone - a self-service "make me QA" toggle on an account that
    /// decides where real money is routed is the exact thing to avoid.
    /// <see cref="FuelFlow.Features.Users.User.IsQaAccount"/> is honoured as a second,
    /// admin-set signal for the same routing decision (a flagged account must not pay live money
    /// just because its phone is missing here), in addition to its older uses (test stock, the
    /// QA gate).
    /// </remarks>
    public IList<string> QaPhones { get; set; } = new List<string>();

    /// <summary>
    /// Safety net for lost or late payment webhooks. When enabled, a recurring job polls
    /// Monobank's invoice-status API for orders still awaiting payment and drives them
    /// through the same transition path a webhook would. Only runs when <see cref="Enabled"/>
    /// is also true (i.e. against the real Monobank client, never the in-process mock, whose
    /// unknown-invoice fallback is a fabricated "success").
    /// </summary>
    public bool ReconciliationEnabled { get; set; } = true;

    /// <summary>
    /// Explicit acknowledgement that Production is running with the reconciliation safety net off.
    /// Checked only by the startup guard (SecurityConfigurationValidator), which refuses to boot in
    /// Production when <see cref="Enabled"/> is true and <see cref="ReconciliationEnabled"/> is false
    /// unless this is set. Running without reconciliation means a lost or late payment webhook is
    /// never recovered, so it must be a recorded decision rather than a silent default.
    /// </summary>
    public bool AcknowledgeReconciliationDisabled { get; set; } = false;

    /// <summary>
    /// Explicit acknowledgement that Production is running against a Monobank *test* merchant
    /// token, which collects no real money. Test tokens are recognisable by their <c>test_</c>
    /// prefix. This is a loud warning rather than a hard refusal, for the same reason as
    /// <see cref="AcknowledgeReconciliationDisabled"/>: the sandbox phase is deliberate today, and
    /// blocking it would stop work rather than make it safer. What must not happen is somebody
    /// believing they are taking money when they are not — so the state has to be written down.
    /// </summary>
    public bool AcknowledgeTestTokenInProduction { get; set; } = false;

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
    /// <remarks>
    /// This map is for key ROTATION of the live merchant, not for the second merchant. A live
    /// and a sandbox webhook are told apart by verifying the signature against each merchant's
    /// own key in turn - ECDSA matches exactly one of them - not by reading a key id out of the
    /// header, since that header identifies which of ONE merchant's keys signed the call.
    /// </remarks>
    public Dictionary<string, string> PublicKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Credentials for <paramref name="merchant"/>, or null when that merchant is not configured.
    /// </summary>
    public MonobankCredentials? CredentialsFor(MonobankMerchant merchant) => merchant switch
    {
        MonobankMerchant.Live => new MonobankCredentials(Token, BaseUrl, PublicKey),
        MonobankMerchant.Sandbox => new MonobankCredentials(SandboxToken, BaseUrl, SandboxPublicKey),
        _ => null
    };

    /// <summary>Whether <paramref name="merchant"/> has a token configured.</summary>
    public bool IsConfigured(MonobankMerchant merchant) =>
        !string.IsNullOrWhiteSpace(CredentialsFor(merchant)?.Token);
}
