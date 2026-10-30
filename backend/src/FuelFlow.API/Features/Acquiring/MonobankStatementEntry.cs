using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;

namespace FuelFlow.Features.Acquiring;

/// <summary>
/// One row of the acquirer's statement, as we stored it.
/// </summary>
/// <remarks>
/// The acquirer's record, not our interpretation of it. Nothing here is derived from an order,
/// and nothing is mapped onto <c>OrderStatus</c>: the whole point of the table is to hold what they
/// said next to what we said, so the two can be compared rather than conflated.
///
/// <para>
/// <see cref="Reference"/> is deliberately not a join key. Checkout sends a human-readable string -
/// "FuelFlow Order {id}" for a single purchase, but literally "FuelFlow Bundle" for every bulk
/// checkout and "FuelFlow Voucher Renewal" for every renewal - so thousands of distinct invoices
/// share the same value. <see cref="InvoiceId"/> is 1:1 with an order and is the only reliable key.
/// </para>
///
/// <para>
/// <see cref="RawPayload"/> is the verbatim row. It is kept because we are still in sandbox and
/// cannot re-fetch a real statement to re-derive it: if a field turns out to matter later, it can be
/// read back out of the rows we already stored instead of being lost for good. It is nullable and
/// not indexed, so it costs nothing to stop writing it.
/// </para>
/// </remarks>
public class MonobankStatementEntry
{
    public Guid Id { get; set; }

    /// <summary>Which merchant's statement this came from. The same acquirer id can exist on both
    /// merchants, so the unique key is scoped by merchant rather than by id alone.</summary>
    public MonobankMerchant Merchant { get; set; }

    /// <summary>
    /// The key this row is filed under. OURS, composed from what the acquirer sends - the statement
    /// carries no id of its own - and stable across re-fetches of the same period, which is what
    /// makes re-importing a month idempotent.
    /// </summary>
    /// <remarks>
    /// Composed by <c>MonobankStatementItem.ComposeStatementId()</c>: the invoice, or failing that
    /// the payment-system id or the masked pan, then the operation time, amount and currency. It is
    /// printed in the act an accountant signs, so it is readable rather than a hash.
    /// </remarks>
    public string StatementId { get; set; } = null!;

    public string? InvoiceId { get; set; }

    /// <summary>Our reference, echoed back. Evidence, not a key - see the type remarks. Indexing it
    /// would suggest it joins, and it does not.</summary>
    public string? Reference { get; set; }

    /// <summary>
    /// The payment purpose as the acquirer shows it on the receipt - what the customer typed, or
    /// what the checkout asked for. Evidence, and never a key: the same string appears on
    /// thousands of unrelated payments, and a match key built on it would match a customer's own
    /// memo to somebody else's invoice.
    /// </summary>
    public string? Destination { get; set; }

    /// <summary>
    /// Kopecks, exactly as Monobank reports them - the acquirer's API speaks kopecks and nothing else,
    /// so this is a boundary value and is not converted on the way in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property used to carry a comment saying "Never a decimal", on the grounds that a conversion
    /// is where the 978.57/979 bug class lives. That reasoning was half right and the conclusion was
    /// wrong: the protection against a conversion is <em>not converting</em>, and a <c>long</c> is an
    /// integer type holding money, which is the thing that goes wrong at scale and invites unit
    /// arithmetic. <c>numeric(14,0)</c> is exact for kopecks - no rounding, no precision loss - and it
    /// makes the type declare the same thing the column name does.
    /// </para>
    /// <para>
    /// The reconciliation still divides by 100 to compare against our own UAH amounts, and that
    /// division stays visible in the code rather than hidden in the type, because both sides of that
    /// comparison being kopecks is exactly what makes it safe.
    /// </para>
    /// </remarks>
    public decimal? AmountKopecks { get; set; }

    /// <summary>Currency code, 980 for UAH.</summary>
    public int? Ccy { get; set; }

    /// <summary>Acquirer-side status, in the acquirer's vocabulary.</summary>
    public string? Status { get; set; }

    /// <summary>The financial operation time (<c>date</c> on the wire). The period the act filters on.</summary>
    public DateTime? AcquirerCreatedAtUtc { get; set; }

    /// <summary>Kept, and empty. The documented statement payload carries one timestamp per row and
    /// no separate modification time, so nothing populates this column today - it stays rather than
    /// being dropped so a future acquirer field does not need a migration to accept.</summary>
    public DateTime? AcquirerModifiedAtUtc { get; set; }

    /// <summary>Kept, and empty, for the same reason as <see cref="AcquirerModifiedAtUtc"/>: the
    /// documented statement row carries no <c>extRef</c>. The value we send on <c>invoice/cancel</c>
    /// is echoed back to us, not to the statement.</summary>
    public string? ExtRef { get; set; }

    /// <summary>The masked pan, from the wire's <c>maskedPan</c>.</summary>
    public string? MaskedCard { get; set; }
    public string? Rrn { get; set; }
    public string? ApprovalCode { get; set; }

    /// <summary>The row exactly as received. See the type remarks.</summary>
    public string? RawPayload { get; set; }

    /// <summary>When we first stored this row.</summary>
    public DateTime FirstSeenAtUtc { get; set; }

    /// <summary>When we last saw it in a fetch. A row that keeps being re-reported is one the
    /// acquirer is still revising, which is different from one that is settled and closed.</summary>
    [MutableAfterInsert]
    public DateTime LastSeenAtUtc { get; set; }

    /// <summary>
    /// Whether the acquirer's status means money was received, matched loosely on purpose.
    /// </summary>
    /// <remarks>
    /// The status column is explicitly not mapped onto our enums - the statement speaks in its own
    /// words and the whole point of the table is to keep them. So this is a substring test, not an
    /// equality test against a closed set: "success" and "hold"/"success_hold" both mean received,
    /// and a vocabulary we have not seen fails closed here rather than being guessed at.
    /// <para>
    /// One definition, used by the import's settlement, the unapplied-receipt booking and the close
    /// check: three callers that must agree on what "money arrived" means, or one of them reports a
    /// difference the other two do not have.
    /// </para>
    /// </remarks>
    public static bool IsReceivedStatus(string? acquirerStatus)
        => !string.IsNullOrWhiteSpace(acquirerStatus)
           && acquirerStatus.Contains("success", StringComparison.OrdinalIgnoreCase);
}