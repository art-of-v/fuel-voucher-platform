using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Vouchers.Redemptions;

/// <summary>
/// One line of a supplier or pump statement, imported once and then reconciled.
/// </summary>
/// <remarks>
/// <para>
/// The row is kept even when it cannot be matched. An unknown voucher number or a litre mismatch is
/// the reason the exception queue exists; dropping the line would hide the discrepancy and leave a
/// sale missing from the books.
/// </para>
/// <para>
/// <see cref="ExternalId"/> is the supplier's line id (or a stable hash of the line). It is unique,
/// so re-importing the same statement is idempotent rather than a second claim on the same fuel.
/// </para>
/// </remarks>
public sealed class RedemptionStatementRow
{
    public Guid Id { get; set; }

    /// <summary>The supplier's stable identifier for this line.</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string VoucherNumber { get; set; } = string.Empty;

    public decimal Liters { get; set; }

    /// <summary>When the supplier says the fuel was taken, if the statement carries a time.</summary>
    public DateTime? RedeemedAtUtc { get; set; }

    /// <summary>The voucher this line matched, when one was found.</summary>
    public Guid? VoucherId { get; set; }

    public FuelVoucher? Voucher { get; set; }

    /// <summary>The active claim this line matched, when one existed.</summary>
    public Guid? VoucherRedemptionId { get; set; }

    public VoucherRedemption? VoucherRedemption { get; set; }

    /// <summary>Where this line stands. See <see cref="RedemptionStatementStatuses"/>.</summary>
    [MutableAfterInsert]
    public RedemptionStatementStatus Status { get; set; }

    /// <summary>The line as the supplier sent it, kept verbatim for the operator.</summary>
    public string? Note { get; set; }

    public DateTime ImportedAtUtc { get; set; }
    public Guid ImportedByUserId { get; set; }

    [MutableAfterInsert]
    public DateTime? ResolvedAtUtc { get; set; }
    [MutableAfterInsert]
    public Guid? ResolvedByUserId { get; set; }
    [MutableAfterInsert]
    public string? ResolutionNote { get; set; }
}

public enum RedemptionStatementStatus
{
    /// <summary>Matched a pending claim; waiting for the operator to confirm or reject it.</summary>
    Matched,

    /// <summary>The operator confirmed the claim this line belongs to.</summary>
    Confirmed,

    /// <summary>The operator rejected the claim this line belongs to.</summary>
    Rejected,

    /// <summary>The confirmed claim was reversed.</summary>
    Reversed,

    /// <summary>No live voucher carries this number.</summary>
    UnknownVoucher,

    /// <summary>The voucher exists but its litre count does not agree with the statement.</summary>
    Mismatch,

    /// <summary>The voucher was already used when this line arrived.</summary>
    AlreadyConfirmed,

    /// <summary>The voucher is in a state that cannot become pending (available, expired, blocked...).</summary>
    NotAssigned,

    /// <summary>An operator closed a line that needed no voucher action.</summary>
    Dismissed,
}

public static class RedemptionStatementStatuses
{
    public static readonly RedemptionStatementStatus[] AwaitingReview =
    [
        RedemptionStatementStatus.Matched,
        RedemptionStatementStatus.UnknownVoucher,
        RedemptionStatementStatus.Mismatch,
        RedemptionStatementStatus.AlreadyConfirmed,
        RedemptionStatementStatus.NotAssigned,
    ];

    public static readonly RedemptionStatementStatus[] All = Enum.GetValues<RedemptionStatementStatus>();

    public static bool AwaitingOperator(RedemptionStatementStatus status) =>
        Array.IndexOf(AwaitingReview, status) >= 0;
}
