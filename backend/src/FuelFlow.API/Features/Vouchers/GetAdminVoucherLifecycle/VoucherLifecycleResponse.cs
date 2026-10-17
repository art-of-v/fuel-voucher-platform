namespace FuelFlow.Features.Vouchers.GetAdminVoucherLifecycle;

/// <summary>One voucher's whole recorded life, as the operator reads it.</summary>
public sealed class VoucherLifecycleResponse
{
    public Guid VoucherId { get; set; }
    public string? VoucherNumber { get; set; }
    public string Status { get; set; } = null!;
    public decimal Liters { get; set; }
    public string Brand { get; set; } = null!;

    /// <summary>
    /// Soft-deleted, not gone. Shown as a fact about the row rather than by returning 404: the archive
    /// flag is itself part of the record, and it has its own event in the timeline below.
    /// </summary>
    public bool IsArchived { get; set; }

    // ---- Who we bought it from, and what it cost. The first line of what the page must answer. ----

    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }

    /// <summary>UAH per liter, stamped by the import batch. Null is "we never recorded what we paid".</summary>
    public decimal? CostPerLiter { get; set; }

    public DateOnly SupplierExpirationDate { get; set; }
    public DateOnly CustomerExpirationDate { get; set; }

    // ---- Where the paper came from. ----

    public Guid? ImportJobId { get; set; }
    public string? ImportFileName { get; set; }
    public DateTime? ImportedAtUtc { get; set; }

    // ---- Lineage. A replace issues a different paper for the same fuel, so one tank of fuel spans ids. ----

    /// <summary>The oldest voucher in the chain — the one whose purchase this all descends from.</summary>
    public Guid RootVoucherId { get; set; }

    /// <summary>Oldest first. The requested voucher is the last entry unless it was never replaced.</summary>
    public List<Guid> Lineage { get; set; } = [];

    /// <summary>Everything, one list, oldest first.</summary>
    public List<VoucherLifecycleEntry> Entries { get; set; } = [];
}

/// <summary>One row of the timeline.</summary>
public sealed class VoucherLifecycleEntry
{
    /// <summary>Which record this came from. See <see cref="VoucherLifecycleSources"/>.</summary>
    public string Source { get; set; } = null!;

    /// <summary>
    /// What happened, in that source's own vocabulary — a movement kind, an event type, or a
    /// projector's <c>Purchase</c>/<c>Renewal</c>. Deliberately not flattened into one invented
    /// vocabulary: the three sources mean different things and a merged noun would hide which is which.
    /// </summary>
    public string Type { get; set; } = null!;

    public DateTime OccurredAtUtc { get; set; }

    // ---- Movements speak in places, events in statuses, money in terms. One pair of columns, three
    // ---- meanings, and Source is what says which. A single "from -> to" that pretended otherwise
    // ---- would be a lie in two of the three cases.

    public string? From { get; set; }
    public string? To { get; set; }

    public DateOnly? PreviousCustomerExpiration { get; set; }
    public DateOnly? NewCustomerExpiration { get; set; }

    /// <summary>Set on paid renewals only.</summary>
    public string? TermCode { get; set; }

    /// <summary>
    /// What was paid. Only the money rows carry it: the ledger deliberately does not copy the price
    /// onto a movement, because <c>order_line_items.line_total</c> already owns that number and a second
    /// copy is a second thing to keep in step.
    /// </summary>
    public decimal? AmountUah { get; set; }

    /// <summary>A movement's own cost movement at the time, when the row carries one.</summary>
    public decimal? CostDeltaUah { get; set; }

    // ---- Who, and when. ----

    public string? ActorKind { get; set; }
    public Guid? ActorUserId { get; set; }

    /// <summary>
    /// Resolved for display from a lookup that may come back empty — the id column is FK-free so the
    /// audit outlives its author. Left null rather than filled with a placeholder, because the id is
    /// still there and still answerable.
    /// </summary>
    public string? ActorName { get; set; }

    public string? Reason { get; set; }

    /// <summary>What the caller can be pointed back at: an order, a batch, a command.</summary>
    public string? ExternalRef { get; set; }

    public Guid? OrderId { get; set; }
}

/// <summary>
/// The three records the timeline interleaves. Not an enum: these are strings on the wire, and a UI
/// switches on them.
/// </summary>
public static class VoucherLifecycleSources
{
    /// <summary>Custody: the paper moved.</summary>
    public const string Movement = "movement";

    /// <summary>State: something was done to the voucher.</summary>
    public const string Event = "event";

    /// <summary>Money: a paid renewal, which the ledger cannot carry because it is not a movement.</summary>
    public const string Money = "money";
}
