using FuelFlow.Features.Vouchers.SharedModels;

namespace FuelFlow.Features.Vouchers.Availability;

/// <summary>
/// The inventory, counted once: one row per
/// (brand, fuel type, litres, status, pool, deleted, expiry date) with the number of vouchers in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for.</b> Two questions are asked of the stock on paths where the answer must not
/// cost a scan of the largest table in the schema:
/// </para>
/// <list type="bullet">
/// <item>the operator's voucher list needs its three dropdowns - the networks, the grades, the sizes
/// that the inventory actually carries (the catalogue is a superset, and a dropdown that offers a
/// combination no voucher has returns an empty page and reads as lost data);</item>
/// <item>the storefront's package list needs the sellable combinations, and it is asked by every
/// anonymous visitor, so its cost is linear in the number of vouchers ever sold.</item>
/// </list>
/// <para>
/// <b>Why it is exact.</b> A trigger on <c>fuel_vouchers</c> keeps it, so it is right at every
/// moment rather than at the last refresh - a summary that is a minute stale would advertise stock
/// the checkout then refuses, which is the exact failure
/// <see cref="PackageStockAvailability"/> exists to prevent. The trigger also catches the writes
/// that do not go through EF: the fulfilment path claims a voucher with a raw UPDATE, and a summary
/// maintained by the application would miss it.
/// </para>
/// <para>
/// <b>Why the dimensions are the ones they are.</b> They are the columns the two readers filter on:
/// the storefront sells <c>Available</c> vouchers of the caller's own pool that are not deleted and
/// not expired, and the operator's list is the whole inventory including the deleted rows. So the
/// summary carries a status, a pool, a deleted flag and an expiry date - not as decoration, but
/// because a reader that cannot filter on them would get a different answer than it gets today.
/// </para>
/// <para>
/// <b>The brand is the text, not the id.</b> The operator's list filters on
/// <c>v.Brand == query.Brand</c> - the raw text - and the storefront folds it to lower case. A
/// summary keyed by the id would have to join the brands table to reproduce either, and the
/// canonical slug is not what the list filters on. The text is what both readers mean by it today,
/// so the text is what is kept.
/// </para>
/// <para>
/// Nothing writes this table from the application: the trigger owns it, and the entity is keyless
/// because it is never updated and never tracked.
/// </para>
/// </remarks>
public sealed class StockSummary
{
    /// <summary>
    /// The row's own key: a uuid, like every other key in the schema.
    /// </summary>
    /// <remarks>
    /// The group itself - the seven columns below - is what makes a row unique, and the database
    /// enforces that with a unique index. This id is here because a row still has to be a row: EF
    /// cannot track a keyless entity, and a key that says nothing is this schema's own rule (see
    /// D-21 - a number taken from a sequence says how many rows came before it, which is a fact
    /// about the past that a reader does not need and cannot un-see).
    /// </remarks>
    public Guid Id { get; set; }

    /// <summary>The network as the voucher rows spell it - the same text the list filters on.</summary>
    public string Brand { get; set; } = null!;

    /// <summary>The grade.</summary>
    public Guid FuelTypeId { get; set; }

    /// <summary>The size, in litres.</summary>
    public decimal Liters { get; set; }

    /// <summary>The voucher's status - the storefront sells only <c>Available</c>.</summary>
    public VoucherStatus Status { get; set; }

    /// <summary>Which pool the vouchers belong to - <c>real</c> or <c>qa</c>.</summary>
    public string DataKind { get; set; } = null!;

    /// <summary>Whether the rows are soft-deleted: the operator sees them, the storefront does not.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>The supplier's expiry date - the storefront does not sell what has expired.</summary>
    public DateOnly SupplierExpirationDate { get; set; }

    /// <summary>How many vouchers are in this group.</summary>
    public int VoucherCount { get; set; }
}
