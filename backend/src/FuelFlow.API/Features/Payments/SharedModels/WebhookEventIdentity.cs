using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.Payments.SharedModels;

/// <summary>
/// Derives a stable identity for a provider event that arrives without one.
/// </summary>
/// <remarks>
/// <para>
/// Monobank's invoice webhook body is <c>{status, amount, createdDate, modifiedDate, invoiceId,
/// cancelList}</c> - there is no event id in it. So a redelivery cannot be rejected on a provider
/// key, and something has to stand in for one.
/// </para>
///
/// <para>
/// <b>Why not hash the raw body.</b> JSON key order and whitespace are not stable across a
/// provider's own tooling changes, and a hash of the bytes would let a byte-different redelivery
/// of the same event through. The two failure modes are opposites - hash the bytes and you miss
/// duplicates, compare a subset and you may merge genuinely different events - and the first is
/// worse: a missed duplicate means two rows for one event, which is the thing being eliminated.
/// </para>
///
/// <para>
/// <b>Why not <c>modifiedDate</c> alone.</b> Two distinct events on one invoice within the same
/// clock tick would collide. Including the status and the invoice narrows it to "same invoice,
/// same status, same instant", which is the level at which the acquirer's own replay protection
/// also operates - the webhook handler's stale-replay guard compares exactly
/// <c>(invoiceId, modifiedDate)</c>.
/// </para>
///
/// <para>
/// <b>What this does not solve, stated plainly.</b> If the acquirer ever emits two genuinely
/// different events for one invoice that share a status and a timestamp, they map to one id and
/// the second is rejected. That is a real ceiling, and it is no worse than the guard it runs
/// alongside: the handler would have discarded the second one anyway on
/// <c>ModifiedDate &lt;= LastWebhookModifiedDateUtc</c>. Hashing the amount into the identity
/// would narrow it, at the cost of letting through a redelivery whose amount field formatting
/// differs - the worse trade, for the reason in the previous paragraph.
/// </para>
///
/// <para>
/// The timestamp is normalised to UTC and formatted with the round-trip pattern so the derivation
/// cannot depend on the server's local zone or culture. Two boxes configured differently must
/// produce the same id for the same event or the uniqueness that justifies this class does not hold.
/// </para>
/// </remarks>
public static class WebhookEventIdentity
{
    /// <summary>The provider token used when none is supplied.</summary>
    public const string Monobank = "monobank";

    /// <summary>
    /// The identity for one delivery, or <see langword="null"/> when there is nothing to derive
    /// from - an event with no invoice and no timestamp cannot be identified, and inventing an id
    /// for it would defeat the point of the unique index.
    /// </summary>
    public static string? Derive(
        string provider,
        string? invoiceId,
        string? providerStatus,
        DateTime? providerModifiedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(invoiceId) && providerModifiedAtUtc is null)
        {
            return null;
        }

        var canonical = string.Join('',
            provider,
            invoiceId ?? string.Empty,
            providerStatus ?? string.Empty,
            providerModifiedAtUtc is null
                ? string.Empty
                : providerModifiedAtUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}