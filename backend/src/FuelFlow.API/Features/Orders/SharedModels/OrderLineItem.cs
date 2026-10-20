namespace FuelFlow.Features.Orders.SharedModels;

using FuelFlow.Features.Vouchers;

public class OrderLineItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Brand { get; set; } = null!;
    public Guid FuelTypeId { get; set; }
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>
    /// Pump-based list total for this line (UAH), frozen at checkout via
    /// <see cref="FuelFlow.SharedKernel.Domain.FuelPricing.OriginalPackagePrice"/>. The customer's
    /// saving vs the pump is <c>OriginalLineTotal - LineTotal</c>. Null for rows predating this
    /// column (no backfill — historical pump price cannot be reconstructed) and for lines with no
    /// pump reference (e.g. voucher renewals), which therefore contribute no saving.
    /// </summary>
    public decimal? OriginalLineTotal { get; set; }

    /// <summary>
    /// The term the customer bought this fuel for (<c>VoucherRenewalTerm.Code()</c>), frozen at
    /// checkout. Null means the full remaining supplier term was sold, which is the behaviour for every
    /// order placed before short terms existed.
    /// </summary>
    /// <remarks>
    /// <b>A renewal line always has one.</b> It did not, until #1179: <c>RenewalCheckoutCommandHandler</c>
    /// wrote the term to <c>voucher_renewal_items</c> and left this column null, so the term the branch
    /// decision is computed from was on a table that has nothing to do with the line it describes. The
    /// value is now written here, <c>voucher_renewal_items</c> is gone, and
    /// <c>ck_order_line_renewal_has_term</c> refuses a renewal line without one.
    /// </remarks>
    public string? TermCode { get; set; }

    /// <summary>
    /// The voucher this line renews, when the line is a renewal. Null on every other kind of line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the answer to "which voucher did the customer mean?", and it has to live on the LINE
    /// rather than on the fulfilment. A renewal is an intent: it exists from checkout, before payment
    /// and before any stock is assigned. A paid renewal that finds no stock has no fulfilment row at
    /// all, and a fulfilment-only home would leave the retry path with nothing to know which voucher it
    /// is retrying.
    /// </para>
    /// <para>
    /// Putting it here also removed the positional pairing that used to be
    /// <c>voucher_renewal_items.line_item_id</c>. That pairing had to be taken at write time because
    /// it was the only moment it was unambiguous, and it could not be reconstructed afterwards: two
    /// renewal lines on the same brand, fuel and volume are indistinguishable after the fact, which is
    /// why the 2026-10-16 backfill left them null. A source on the line is not a pairing that has to
    /// survive; it is part of what the line <em>is</em>.
    /// </para>
    /// </remarks>
    public Guid? SourceVoucherId { get; set; }

    /// <summary>
    /// Which fulfilment path this renewal line took: <c>extend</c> or <c>replace</c>, as
    /// <see cref="VoucherRenewalBranchCodes"/>. Null on every other kind of line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stored rather than derived, and it has to be. The branch is <b>decided and priced at
    /// checkout</b> from the voucher's state that day; fulfilment deliberately re-decides it from the
    /// current state, because the voucher can lapse in between. So "what branch was this" is a
    /// time-varying derivation, and anything reading it back after the fact - the customer's history
    /// timeline, which says "replaced" instead of mislabelling a swap as an extension - cannot
    /// recompute the original answer.
    /// </para>
    /// <para>
    /// <c>operator_voucher_renewals.branch</c> already stores it for the operator path, with the same
    /// two words. One vocabulary, three tables that must agree.
    /// </para>
    /// </remarks>
    public string? RenewalBranch { get; set; }

    /// <summary>
    /// The customer's expiry on the source voucher BEFORE this renewal was applied. Null until
    /// fulfilment, and null on every non-renewal line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A snapshot, because there is nothing left to read it from afterwards. The extend branch
    /// overwrites <c>fuel_vouchers.customer_expiration_date</c> in place through a compare-and-swap
    /// UPDATE, and no history column survives it — so once the renewal is applied, the date the
    /// customer started with exists only here.
    /// </para>
    /// <para>
    /// It is what makes the customer's history able to say "valid 5 Nov → 20 Dec" rather than just
    /// showing today's date, which is the question the renewal screen exists to answer.
    /// </para>
    /// </remarks>
    public DateOnly? PreviousCustomerExpiration { get; set; }

    /// <summary>
    /// The expiry this renewal gave the customer. Null until fulfilment, and null on every non-renewal
    /// line.
    /// </summary>
    /// <remarks>
    /// <b>On a replace this is the PROMISE, not the replacement voucher's real life.</b> What the
    /// customer bought is what they get to rely on; any longer real life on the stock voucher is a
    /// hidden reserve they may extend again later, and showing it here would be selling twice.
    /// </remarks>
    public DateOnly? NewCustomerExpiration { get; set; }

    /// <summary>The voucher this line renews. Null unless <see cref="RenewalBranch"/> is set.</summary>
    public FuelVoucher? SourceVoucher { get; set; }

    /// <summary>
    /// The inputs that produced <see cref="UnitPrice"/>, frozen at checkout, as a JSON object:
    /// the per-litre cost, the margin, the pump price, the minimum discount and the terms id this
    /// line was priced under.
    /// </summary>
    /// <remarks>
    /// <see cref="UnitPrice"/> records what the customer paid; this records <em>why</em>. Without it
    /// the answer to "why was this order 89.06?" is only available at the moment of checkout, and
    /// after a contract changes it is gone — the live contract is not the one that was in force when
    /// the order was placed, so it cannot be used to reconstruct the basis.
    /// <para>
    /// <b>Null for every order placed before this column existed, and deliberately not backfilled.</b>
    /// Backfilling from today's contract or today's fuel configuration would manufacture a history
    /// that never happened, which is worse than an honest gap: it looks like evidence.
    /// </para>
    /// </remarks>
    public string? PricingBasis { get; set; }

    public Order Order { get; set; } = null!;
}
