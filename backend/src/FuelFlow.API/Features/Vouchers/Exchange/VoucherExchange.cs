using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Vouchers.Exchange;

/// <summary>
/// One row per OLD stock voucher retired in an operator→supplier exchange (planning #104). When the
/// operator renews our own lapsing stock with the supplier off-platform (no Monobank, no in-app
/// charge), the admin app records the exchange: which old stock voucher was replaced by which new
/// one, the доплата paid, and the cost/liter applied to the new batch.
///
/// A single confirm action retires N old vouchers and activates M new ones; every old voucher in that
/// action shares one <see cref="ExchangeBatchId"/>. Pairing is flexible (1:1 within the same
/// brand/fuel/liters bucket): a paired old voucher stores the new voucher it was replaced by in
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

    /// <summary>The settlement this pair belongs to. See <see cref="MovementBatch"/>.</summary>
    public MovementBatch? MovementBatch { get; set; }

    /// <summary>The retired stock voucher (FK → fuel_vouchers, Restrict).</summary>
    public Guid OldVoucherId { get; set; }

    /// <summary>
    /// The new stock voucher this old one was replaced by, or null when the old voucher was not paired
    /// (more old than new in its bucket). Nullable - but constrained like the retired side (#217), so
    /// deleting a replacement voucher cannot silently orphan this audit row.
    /// </summary>
    public Guid? NewVoucherId { get; set; }

    /// <summary>Fuel of the retired voucher (and of its paired replacement — same bucket).</summary>
    public Guid FuelTypeId { get; set; }

    /// <summary>Brand of the retired voucher (okko/wog/…), denormalized for audit.</summary>
    public string Brand { get; set; } = null!;

    /// <summary>
    /// The brand's id - the key the text above is a copy of.
    /// </summary>
    /// <remarks>
    /// The brand is written twice on this row: as the name the application and the importers speak,
    /// and - from the key - as the id of the brand itself. The name is what a report groups by and
    /// what a parser reads; the id is what the database can check. A composite foreign key over
    /// (fuel type, brand) says the two copies are the same fact; a trigger can only ask.
    /// <para>
    /// The key is required, and the database says so: the composite foreign key
    /// <c>(fuel_type_id, brand_id) → fuel_types(id, brand_id)</c> is what refuses a row whose brand
    /// is not its fuel's brand. A nullable key would be a key the database does not check, because a
    /// foreign key with a NULL in it is not compared at all.
    /// </para>
    /// </remarks>
    public Guid BrandId { get; set; }

    /// <summary>
    /// This row's OWN share of the batch surcharge - the brand charge spread pro-rata over this
    /// pair's liters (see ConfirmVoucherExchangeCommandHandler), NOT the batch total repeated.
    ///
    /// Do not read one row and call it the batch charge: a four-voucher exchange would report a
    /// quarter of what was actually paid. Sum the rows of an ExchangeBatchId.
    ///
    /// This comment used to read "replicated per row", which says the opposite and invites exactly
    /// that bug. The batch cost timeline shipped it once already (#178) and it cost real money.
    /// </summary>
    public decimal SurchargeUah { get; set; }

    /// <summary>The cost/liter set on the new fuel batch for this exchange (old blended + доплата/liter), when known.</summary>
    public decimal? CostPerLiterApplied { get; set; }

/// <summary>
    /// The supplier the old vouchers were bought from and the new ones were obtained from. One exchange
    /// involves exactly one supplier, so every row of a batch carries the same value — but it is recorded
    /// per row because this table is also read one voucher at a time during supplier reconciliation, where
    /// the row alone has to answer "who do we settle with for this voucher".
    /// </summary>
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Admin who recorded the exchange.</summary>
    public Guid ActingUserId { get; set; }

    /// <summary>Admin display name at the time, for audit.</summary>
    public string? ActingUserName { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>The retired stock voucher.</summary>
    public FuelVoucher? OldVoucher { get; set; }

    public FuelVoucher? NewVoucher { get; set; }
}
