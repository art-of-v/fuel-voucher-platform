import {
  codedErrorMessage,
  type CodedSentence,
} from "@/lib/coded-error-message";

type Translator = (key: string, ...params: (string | number)[]) => string;

/**
 * The sentences for a refused redemption action or statement import.
 *
 * The codes come from `RedemptionRefusalCodes`; a code this file has not learnt keeps the server's
 * English sentence. The import refusals are the ones an operator meets with a pasted statement in
 * hand, so the panel says them rather than printing prose written for the log.
 */
export function redemptionRefusalMessage(
  error: unknown,
  t: Translator,
): string {
  return codedErrorMessage(error, t, REDEMPTION_REFUSALS);
}

const REDEMPTION_REFUSALS: Record<string, CodedSentence> = {
  statement_empty: (_error, t) => t("redemption.error.statementEmpty"),
  statement_line_id_required: (_error, t) =>
    t("redemption.error.lineIdRequired"),
  statement_line_id_too_long: (_error, t) =>
    t("redemption.error.lineIdTooLong"),
  statement_line_voucher_required: (_error, t) =>
    t("redemption.error.lineVoucherRequired"),
  statement_line_voucher_too_long: (_error, t) =>
    t("redemption.error.lineVoucherTooLong"),
  statement_line_litres_not_positive: (_error, t) =>
    t("redemption.error.lineLitresNotPositive"),
  redemption_action_invalid: (_error, t) => t("redemption.error.actionInvalid"),
  redemption_not_found: (_error, t) => t("redemption.error.notFound"),
  redemption_invalid_state: (_error, t) => t("redemption.error.invalidState"),
  statement_line_not_found: (_error, t) => t("redemption.error.lineNotFound"),
  statement_line_not_dismissable: (_error, t) =>
    t("redemption.error.lineNotDismissable"),
  statement_line_has_pending_claim: (_error, t) =>
    t("redemption.error.lineHasPendingClaim"),
  statement_line_changed: (_error, t) => t("redemption.error.lineChanged"),
};
