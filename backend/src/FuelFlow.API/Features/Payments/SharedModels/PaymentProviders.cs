namespace FuelFlow.Payments.SharedModels;

/// <summary>
/// Who said the money arrived. <c>payments.provider</c> already existed for the acquirer; this is the
/// vocabulary for it, so a second channel cannot arrive as a free-text string nobody validates.
/// </summary>
/// <remarks>
/// The distinction is load-bearing rather than cosmetic. An acquirer-driven payment is evidence the
/// acquirer produced: it has an invoice, it has a webhook, and a human never touched it — which is
/// what <c>ck_payments_refund_audit_only_on_refund</c> encodes. A declared payment is the opposite:
/// a person states that money arrived, so it must carry who stated it, and a capture without an
/// author would be unattributable money sitting in the ledger.
/// </remarks>
public static class PaymentProviders
{
    /// <summary>
    /// The payment acquirer. Every payment that arrives this way was produced by a provider webhook
    /// and has an acquirer invoice behind it.
    /// </summary>
    public const string Monobank = WebhookEventIdentity.Monobank;

    /// <summary>
    /// Money stated by a person rather than reported by an acquirer — a settlement message the finance
    /// team declares. There is no invoice and no webhook: the evidence is who said so, which is why
    /// these rows must carry an author and must keep <c>order_id</c> nullable.
    /// </summary>
    public const string Declared = "declared";

    /// <summary>True for a payment the acquirer produced, which is what the audit constraint scopes to.</summary>
    public static bool IsAcquirer(string? provider) =>
        string.Equals(provider, Monobank, StringComparison.Ordinal);
}