namespace FuelFlow.SharedKernel.Options;

public sealed class MonobankOptions
{
    public const string SectionName = "Monobank";

    /// <summary>
    /// How long a call to the acquirer may take before it is given up on.
    /// </summary>
    /// <remarks>
    /// The default for an HTTP client is a hundred seconds, and the acquirer's invoice call used
    /// to inherit it: a slow acquirer held a request thread for a minute and a half. The checkout
    /// also depends on this number - the request that loses the race waits for the winner's
    /// invoice, and that wait is bounded by what the acquirer can do to us - so it is stated here
    /// rather than left to a default nobody chose.
    /// </remarks>
    public const int ClientTimeoutSeconds = 15;

    public string Token { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.monobank.ua";
    public string PublicKey { get; set; } = string.Empty;
    public bool Enabled { get; set; } = false;
    public string RedirectUrl { get; set; } = string.Empty;

    /// <summary>
    /// Sandbox merchant token, used only for QA accounts. Separate merchant profile, separate
    /// (nonexistent) settlement, and - unlike the key - a genuinely different identity.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT defaulted to <see cref="Token"/>. If a QA account's routing resolves to
    /// the sandbox and this is empty, the correct behaviour is to refuse the checkout - not to
    /// quietly send the customer to the live merchant, which would take real money from someone
    /// who is testing. See <c>MonobankMerchantResolver</c>, which fails closed on that case.
    /// <para>
    /// There is deliberately no <c>SandboxPublicKey</c>. Monobank signs webhooks with a
    /// company-wide key, identical for the live and the sandbox token of the same company, so a
    /// second copy could only ever be a copy that is wrong.
    /// </para>
    /// </remarks>
    public string SandboxToken { get; set; } = string.Empty;

    /// <summary>
    /// Server-side allowlist of QA accounts, as full E.164 phone numbers, comma-separated.
    /// </summary>
    /// <remarks>
    /// A string rather than a list on purpose: this is supplied per environment as an environment
    /// variable, and the configuration binder cannot fill a list from a single
    /// <c>Monobank__QaPhones=+380..,+380..</c> value - it binds an empty list and says nothing.
    /// An allowlist that silently contains nobody is worse than one that is visibly empty, because
    /// the effect is not "no QA accounts", it is "QA accounts quietly routed to the live merchant".
    /// <para>
    /// This is the primary authority for which merchant a checkout may touch. It lives in deploy
    /// configuration rather than in a user-profile flag so that it cannot be edited from the
    /// admin panel or the API by anyone - a self-service "make me QA" toggle on an account that
    /// decides where real money is routed is the exact thing to avoid.
    /// <see cref="FuelFlow.Features.Users.User.IsQaAccount"/> is honoured as a second,
    /// admin-set signal for the same routing decision (a flagged account must not pay live money
    /// just because its phone is missing here), in addition to its older uses (test stock, the
    /// QA gate).
    /// </para>
    /// </remarks>
    public string? QaPhones { get; set; }

    /// <summary>
    /// <see cref="QaPhones"/> split into entries, with blanks dropped.
    /// </summary>
    public IReadOnlyList<string> QaPhoneList =>
        (QaPhones ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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

    /// <summary>Grace period before an unpaid order is polled, so the webhook is given time to arrive first.</summary>
    public int ReconciliationMinAgeMinutes { get; set; } = 3;

    /// <summary>Orders older than this are no longer polled; their Monobank invoices have long since expired.</summary>
    public int ReconciliationMaxAgeHours { get; set; } = 24;

    /// <summary>Maximum orders reconciled per run, bounding the Monobank status calls per cycle.</summary>
    public int ReconciliationBatchSize { get; set; } = 100;

    /// <summary>
    /// How far back each statement import re-reads, in days.
    /// </summary>
    /// <remarks>
    /// A rolling window rather than a per-calendar-month fetch, because an acquirer can revise or
    /// settle a transaction days after we first saw it. A month-scoped fetch would never look at
    /// that row again, so the correction would be invisible forever - and an invisible correction is
    /// exactly the thing this table exists to make visible.
    ///
    /// The default of 35 covers a calendar month with a week of slack on either side, so a run that
    /// was missed, a deploy, or a delayed settlement all heal on the next pass. Note the two settings
    /// are independent on purpose: the import window is about catching late movement, while the
    /// reconciliation period stays a calendar month, because "October's act" has to mean October to
    /// whoever reads it.
    ///
    /// Wider than the acquirer's own limit, deliberately: it rejects a statement request covering
    /// more than 31 days outright, so the import fetches this window as consecutive slices rather
    /// than as one request (see <c>MonobankStatementWindow</c>). Shortening the window below that
    /// limit is the wrong fix - it would drop the oldest days of every pass instead of reading them -
    /// and setting it to zero or less asks for no period at all, which the job says out loud rather
    /// than reporting a healthy pass that found nothing.
    /// </remarks>
    public int StatementImportWindowDays { get; set; } = 35;

    /// <summary>
    /// Whether the statement import runs at all. Shares the acquiring switch rather than adding its
    /// own: there is nothing to import when Monobank is disabled, and the in-process mock returns
    /// synthetic rows that match no order, so importing them would fill the table with noise that
    /// every later reconciliation has to filter out.
    /// </summary>
    public bool StatementImportEnabled { get; set; } = true;

    /// <summary>
    /// Optional map of key id → public key, used when Monobank signs webhooks with the
    /// X-Key-Id header (keys rotate). When X-Key-Id is absent, <see cref="PublicKey"/> is used.
    /// </summary>
    /// <remarks>
    /// This map is for key ROTATION, not for the second merchant. Monobank signs with one
    /// company-wide key, so a sandbox webhook resolves through the same entry points as a live
    /// one; which merchant an invoice belongs to comes from the stored order, never from the key.
    /// </remarks>
    public Dictionary<string, string> PublicKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Credentials for <paramref name="merchant"/>, or null when that merchant is not configured.
    /// </summary>
    public MonobankCredentials? CredentialsFor(MonobankMerchant merchant) => merchant switch
    {
        MonobankMerchant.Live => new MonobankCredentials(Token, BaseUrl, PublicKey),
        MonobankMerchant.Sandbox => new MonobankCredentials(SandboxToken, BaseUrl, PublicKey),
        _ => null
    };

    /// <summary>Whether <paramref name="merchant"/> has a token configured.</summary>
    public bool IsConfigured(MonobankMerchant merchant) =>
        !string.IsNullOrWhiteSpace(CredentialsFor(merchant)?.Token);
}
