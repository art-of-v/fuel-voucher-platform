namespace FuelFlow.Payments.SharedModels;

using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settlement.SharedModels;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;

/// <summary>
/// One payment, as a thing with its own identity - rather than four columns on the order.
/// </summary>
/// <remarks>
/// <para>
/// Before this table existed, a payment was <c>orders.monobank_invoice_id</c>,
/// <c>monobank_payment_url</c>, <c>monobank_status</c> and a pair of webhook timestamps. That
/// cannot answer three questions a business asks about money:
/// </para>
/// <list type="bullet">
/// <item><b>When did the customer actually pay?</b> There was no paid-at timestamp at all.
/// <see cref="Order.MonobankStatus"/> says <em>that</em> they paid;
/// <c>LastWebhookProcessedAtUtc</c> says <em>when we last heard from the acquirer</em>, which is a
/// different question and drifts - an "expired" callback a week later moves it without any money
/// changing hands. For a VAT payer the date is the reporting period boundary, so "roughly when"
/// is not a thing that can be carried.</item>
///
/// <item><b>How many times did they try to pay?</b> <c>monobank_invoice_id</c> is a single column
/// and it is unique, so re-issuing a payment link overwrites the previous invoice id. Two invoices
/// then exist at the acquirer while the database knows about one, and which of them the customer
/// used is unrecoverable.</item>
///
/// <item><b>What did the acquirer say, and did we act on it?</b> That evidence lived in
/// <c>WebhookEvent</c>, in the log, and nowhere else.</item>
/// </list>
///
/// <para>
/// <b>Scope of this slice.</b> A capture is written when an invoice is created and updated when a
/// webhook moves it. Refund linkage is modelled - <see cref="Kind"/> carries <c>Refund</c> and
/// <see cref="RefundId"/> is there - but no code writes it yet: the refund state machine has its
/// own handlers and its own tests, and folding it in here would be a larger change than the
/// evidence gap it closes. The schema does not need to move again when that happens.
/// </para>
///
/// <para>
/// <b>Both money columns are stored on purpose.</b> <see cref="AmountUah"/> is what we billed and
/// <see cref="AmountKopecks"/> is what the wire said. Storing only one would make a mismatch
/// unreconstructable after the fact, and the mismatch check in the webhook handler is the one place
/// where a customer's charge and our price can silently disagree. The database keeps the two
/// consistent with a CHECK rather than trusting the writer to convert correctly.
/// </para>
/// </remarks>
public sealed class Payment
{
    public Guid Id { get; set; }

    /// <summary>
    /// The order this payment settles, or <see langword="null"/> when it is not yet attached to one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nullable because money does not wait for an order. A customer can pay first and order later, and
    /// a column that cannot hold that state forces the money to be recorded somewhere else - or, worse,
    /// not at all until an order happens to exist.
    /// </para>
    /// <para>
    /// <b>A NULL here is not an error and not a pending payment.</b> It is money we hold that is not yet
    /// against a sale: an advance, which is a liability, not revenue. Revenue is recognised when the fuel
    /// is delivered, never on receipt. Posting such a row into a sales account would report something
    /// the business has not earned and would put VAT on the wrong side.
    /// </para>
    /// <para>
    /// Every acquirer-driven capture still carries one. These are payments declared outside the
    /// acquirer, where the invoice and the order are not the same thing.
    /// </para>
    /// </remarks>
    public Guid? OrderId { get; set; }

    /// <summary>
    /// The customer this money belongs to, when we know who paid. NULL alongside a NULL
    /// <see cref="OrderId"/> means the payment is <em>unallocated</em>: the money is real and belongs
    /// to nobody yet, and an operator decides whose it is.
    /// </summary>
    public Guid? UserId { get; set; }

    public PaymentKind Kind { get; set; } = PaymentKind.Capture;

    /// <summary>
    /// The capture a refund reverses. Null on a capture. Self-referential rather than an order-level
    /// link so that a refund is always answerable as "money back against this exact charge".
    /// </summary>
    public Guid? ParentPaymentId { get; set; }

    /// <summary>The acquiring brand. Lowercase, matching the Monobank vendor token.</summary>
    public string Provider { get; set; } = null!;

    /// <summary>
    /// The acquirer's invoice id. This is the identity that survives a re-issued payment link,
    /// which <c>orders.monobank_invoice_id</c> does not - it is unique per provider and per kind,
    /// so two attempts at paying the same order coexist instead of overwriting one another.
    /// </summary>
    public string? ProviderInvoiceId { get; set; }

    /// <summary>
    /// The acquirer's own payment-system id where it provides one. Distinct from the invoice id:
    /// several payments can be attempted against one invoice, and Monobank's statement rows carry
    /// this value as <c>rrn</c>/<c>approvalCode</c> rather than an invoice id.
    /// </summary>
    public string? ProviderPaymentId { get; set; }

    public MonobankMerchant? Merchant { get; set; }

    /// <summary>What we billed, in UAH. Same type as <c>orders.price</c>.</summary>
    public decimal AmountUah { get; set; }

    /// <summary>What the wire said, in kopecks. Same type as <c>refunds.amount_kopecks</c>.</summary>
    public decimal AmountKopecks { get; set; }

    /// <summary>
    /// What the acquirer actually reported charging, once a callback or a statement row says so.
    /// Null until one arrives.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not constrained against <see cref="AmountKopecks"/>.</b> The two disagreeing is
    /// the entire point of the column: it is how an amount mismatch becomes a queryable fact on the
    /// money row instead of a log line that is gone by the morning. A CHECK asserting they agree
    /// would forbid the one state this table exists to make visible, and would also refuse the write
    /// that discovered it - losing the evidence at the moment it appears. The amount-unit CHECK does
    /// constrain <see cref="AmountKopecks"/> against <see cref="AmountUah"/>; this one is a separate
    /// claim by the acquirer, not a second representation of ours.
    /// </remarks>
    public decimal? ProviderAmountKopecks { get; set; }

    public string Currency { get; set; } = "UAH";

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>
    /// When the money moved. Written from the acquirer's statement when one arrives, because the
    /// statement is the authority on time as well as amount - the webhook carries a modification
    /// date, which is close but is not the financial operation time.
    /// </summary>
    public DateTime? PaidAtUtc { get; set; }

    /// <summary>
    /// The delivery that proved this payment, when the proof was a webhook. A payment can be
    /// settled from the statement instead, and then this stays null - which is itself the answer to
    /// "did the acquirer tell us, or did we find out on our own?".
    /// </summary>
    public Guid? WebhookEventId { get; set; }

    /// <summary>The acquirer's row verbatim, when we have one.</summary>
    public string? ProviderPayload { get; set; }

    /// <summary>
    /// Who approved the refund, and whether it was automatic. Audit of the decision, not of the
    /// money - only a refund has one, and the database refuses these on a capture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These lived on <c>refunds</c>, which is the table this design is dissolving into
    /// <see cref="Kind"/>. They moved here rather than being dropped because "who authorised
    /// returning this customer's money" is a question an auditor asks, and a payment row is the
    /// only place left that can answer it once there is one book instead of two.
    /// </para>
    /// <para>
    /// <see cref="CreatedByUserName"/> is a denormalised copy of the name at approval time. The id
    /// is the real reference and can be restricted against a live user; the name is kept so the
    /// audit still reads correctly after a user is renamed, and is never a join target.
    /// </para>
    /// </remarks>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>The approver's name as it read at approval time. Not a foreign key.</summary>
    public string? CreatedByUserName { get; set; }

    /// <summary>The refund was raised by a system rule rather than by a person.</summary>
    public bool IsAutomatic { get; set; }

    /// <summary>Why the refund could not be completed, when it could not.</summary>
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Order? Order { get; set; }
    public User? User { get; set; }

    /// <summary>
    /// The channel messages that produced this payment. A collection and not a single reference,
    /// because the relationship is genuinely one-to-many and asserting one would be a guess: one
    /// message makes one payment, but a payment from an acquirer has none at all.
    /// </summary>
    public ICollection<SettlementMessage> SettlementMessages { get; set; } = new List<SettlementMessage>();
}

public enum PaymentKind
{
    /// <summary>Money in.</summary>
    Capture,

    /// <summary>Money back, against a capture.</summary>
    Refund
}

/// <remarks>
/// <see cref="Succeeded"/> is the only state in which money is ours, and it is the only one the
/// database requires a timestamp for. The pairing is a CHECK rather than a convention because
/// "paid but with no paid-at" is a state that makes a report silently lie. The reverse direction is
/// deliberately left free: a reversed payment really was received before it was returned, and
/// <see cref="Cancelled"/> with a <c>PaidAtUtc</c> is true rather than contradictory.
/// </remarks>
public enum PaymentStatus
{
    /// <summary>An invoice exists at the acquirer; nobody has paid it.</summary>
    Pending,

    /// <summary>Paid. The only status the revenue side counts.</summary>
    Succeeded,

    /// <summary>The acquirer refused it.</summary>
    Failed,

    /// <summary>Money was taken and then returned by the acquirer.</summary>
    Cancelled,

    /// <summary>The invoice's validity window elapsed with no payment.</summary>
    Expired
}