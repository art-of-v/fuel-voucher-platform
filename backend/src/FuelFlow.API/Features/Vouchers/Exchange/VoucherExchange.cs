using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Vouchers.Exchange;

/// <summary>
/// One row per OLD stock voucher retired in an operator→provider exchange (planning #104). When the
/// operator renews our own lapsing stock with the provider off-platform (no Monobank, no in-app
/// charge), the admin app records the exchange: which old stock voucher was replaced by which new
/// one, the доплата paid, and the cost/liter applied to the new batch.
///
/// A single confirm action retires N old vouchers and activates M new ones; every old voucher in that
/// action shares one <see cref="ExchangeBatchId"/>. Pairing is flexible (1:1 within the same
/// provider/fuel/liters bucket): a paired old voucher stores the new voucher it was replaced by in
/// <see cref="NewVoucherId"/>; an unpaired old voucher simply expires with a null
/// <see cref="NewVoucherId"/>. Unpaired NEW vouchers just enter stock and get no row here.
///
/// The customer-facing "видано на заміну талону #&lt;old&gt;" note is DERIVED from this table — no
/// column is added to the hot <c>fuel_vouchers</c> table.
/// </summary>
public sealed class VoucherExchange
{
    public Guid Id { get; set; }

    /// <summary>Groups every old voucher retired in one confirm action.</summary>
    public Guid ExchangeBatchId { get; set; }

    /// <summary>The retired stock voucher (FK → fuel_vouchers, Restrict).</summary>
    public Guid OldVoucherId { get; set; }

    /// <summary>
    /// The new stock voucher this old one was replaced by, or null when the old voucher was not paired
    /// (more old than new in its bucket). Bare column — no FK: the new voucher is also a fuel_vouchers
    /// row but this stays a nullable audit link, mirroring <c>VoucherRenewalItem.FulfilledVoucherId</c>.
    /// </summary>
    public Guid? NewVoucherId { get; set; }

    /// <summary>Fuel of the retired voucher (and of its paired replacement — same bucket).</summary>
    public string FuelTypeId { get; set; } = string.Empty;

    /// <summary>Provider of the retired voucher (okko/wog/…), denormalized for audit.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The доплata total paid to the provider for this exchange batch, replicated per row for audit.</summary>
    public decimal SurchargeUah { get; set; }

    /// <summary>The cost/liter set on the new fuel batch for this exchange (old blended + доплата/liter), when known.</summary>
    public decimal? CostPerLiterApplied { get; set; }

    /// <summary>
/// Optional provider накладна / invoice number.</summary>
    public string? InvoiceNumber { get; set; }

    /// <summary>
    /// The supplier the old vouchers were bought from and the new ones were obtained from. One exchange
    /// involves exactly one supplier, so every row of a batch carries the same value — but it is recorded
    /// per row because this table is also read one voucher at a time during supplier reconciliation, where
    /// the row alone has to answer "who do we settle with for this voucher".
    /// </summary>
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Optional provider накладна / invoice date.</summary>
    public DateOnly? InvoiceDate { get; set; }

    /// <summary>Admin who recorded the exchange.</summary>
    public Guid ActingUserId { get; set; }

    /// <summary>Admin display name at the time, for audit.</summary>
    public string? ActingUserName { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>The retired stock voucher.</summary>
    public FuelVoucher? OldVoucher { get; set; }
}
