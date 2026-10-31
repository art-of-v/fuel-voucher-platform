namespace FuelFlow.Features.Vouchers.Redemptions;

/// <summary>
/// The stable codes a redemption action or statement import is refused under, so the admin can say
/// the reason in the operator's language instead of printing the English sentence.
/// </summary>
internal static class RedemptionRefusalCodes
{
    public const string StatementEmpty = "statement_empty";

    public const string LineIdRequired = "statement_line_id_required";

    public const string LineIdTooLong = "statement_line_id_too_long";

    public const string LineVoucherRequired = "statement_line_voucher_required";

    public const string LineVoucherTooLong = "statement_line_voucher_too_long";

    public const string LineLitresNotPositive = "statement_line_litres_not_positive";

    public const string ActionInvalid = "redemption_action_invalid";

    public const string RedemptionNotFound = "redemption_not_found";

    public const string RedemptionInvalidState = "redemption_invalid_state";

    public const string StatementLineNotFound = "statement_line_not_found";

    public const string StatementLineNotDismissable = "statement_line_not_dismissable";

    public const string StatementLineHasPendingClaim = "statement_line_has_pending_claim";

    public const string StatementLineChanged = "statement_line_changed";
}
