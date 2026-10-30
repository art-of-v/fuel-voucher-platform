using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Vouchers.Redemptions;

/// <summary>
/// One claim that a voucher was used, and the evidence that closed it.
/// </summary>
/// <remarks>
/// <para>
/// The voucher's status is the current answer; this row is the history of how that answer was
/// reached. A customer's report creates a <see cref="VoucherRedemptionStatus.Reported"/> row and
/// moves the voucher to <c>PendingReconciliation</c>. The row becomes
/// <see cref="VoucherRedemptionStatus.Confirmed"/> only when an operator accepts it, which is also
/// when the provisional journal drafts are posted.
/// </para>
/// <para>
/// The partial unique index in <see cref="Configurations.VoucherRedemptionConfiguration"/> allows
/// one active row per voucher. Older rows stay for the audit: a rejected or withdrawn report is a
/// fact that happened, and a later statement may open a new row.
/// </para>
/// </remarks>
public sealed class VoucherRedemption
{
    public Guid Id { get; set; }

    /// <summary>The voucher this claim is about.</summary>
    public Guid VoucherId { get; set; }

    public FuelVoucher? Voucher { get; set; }

    /// <summary>Where the claim stands. See <see cref="VoucherRedemptionStatuses"/>.</summary>
    [MutableAfterInsert]
    public VoucherRedemptionStatus Status { get; set; }

    /// <summary>
    /// The litres the reporter or the statement claimed. Normally equal to the voucher's own litres;
    /// a difference is precisely what the operator has to look at.
    /// </summary>
    public decimal ReportedLiters { get; set; }

    /// <summary>When the fuel was actually taken, as far as the evidence says.</summary>
    public DateTime RedeemedAtUtc { get; set; }

    /// <summary>When this claim entered the system.</summary>
    public DateTime ReportedAtUtc { get; set; }

    /// <summary>
    /// The customer who self-reported it. NULL means the claim came from a supplier statement
    /// rather than from the holder - an exception the operator must review.
    /// </summary>
    public Guid? ReportedByUserId { get; set; }

    /// <summary>The supplier's line reference, when the claim came from a statement.</summary>
    public string? SupplierReference { get; set; }

    [MutableAfterInsert]
    public DateTime? ConfirmedAtUtc { get; set; }
    [MutableAfterInsert]
    public Guid? ConfirmedByUserId { get; set; }

    /// <summary>When the claim was withdrawn, rejected or reversed.</summary>
    [MutableAfterInsert]
    public DateTime? ClosedAtUtc { get; set; }
    [MutableAfterInsert]
    public Guid? ClosedByUserId { get; set; }

    /// <summary>Why the operator closed it, in their own words.</summary>
    [MutableAfterInsert]
    public string? ResolutionNote { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    [MutableAfterInsert]
    public DateTime UpdatedAtUtc { get; set; }
}

public enum VoucherRedemptionStatus
{
    /// <summary>Reported, not yet confirmed. The voucher is pending and the sale is a draft.</summary>
    Reported,

    /// <summary>The evidence was accepted. The sale is posted.</summary>
    Confirmed,

    /// <summary>The customer withdrew the report before it was confirmed.</summary>
    Withdrawn,

    /// <summary>An operator refused the report. The voucher returns to Assigned.</summary>
    Rejected,

    /// <summary>A confirmed redemption was undone by an operator, in an open period.</summary>
    Reversed,
}

public static class VoucherRedemptionStatuses
{
    public static readonly VoucherRedemptionStatus[] Active =
    [
        VoucherRedemptionStatus.Reported,
        VoucherRedemptionStatus.Confirmed,
    ];

    public static readonly VoucherRedemptionStatus[] All = Enum.GetValues<VoucherRedemptionStatus>();

    public static bool IsActive(VoucherRedemptionStatus status) => Array.IndexOf(Active, status) >= 0;
}
