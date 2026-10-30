import type { ApiError } from "@/lib/api-client";
import {
  codedErrorMessage,
  type CodedSentence,
} from "@/lib/coded-error-message";

type Translator = (key: string, ...params: (string | number)[]) => string;

/**
 * The sentences for the voucher refusals an operator hits on the archive buttons.
 *
 * These were the loudest English on a Ukrainian panel, because they are the refusals the operator
 * meets on purpose: archiving a paid voucher is refused by design (#217), and "delete all" archives
 * the stock it may and keeps the paid ones. The backend says all of that in English, so the reason
 * travels as a code plus the counts, the voucher's number and which signal stopped it, and the words
 * are made here in the panel's own language.
 *
 * A code this file has not learnt, or one whose payload did not arrive, keeps the server's sentence.
 */
export function voucherRefusalMessage(error: unknown, t: Translator): string {
  return codedErrorMessage(error, t, VOUCHER_REFUSALS);
}

/** "it has already been used" — the clause that follows the voucher's number. */
const GAP_KEYS: Record<string, string> = {
  assigned: "vouchers.error.gapAssigned",
  pending_reconciliation: "vouchers.error.gapPendingReconciliation",
  used: "vouchers.error.gapUsed",
  expired: "vouchers.error.gapExpired",
  blocked: "vouchers.error.gapBlocked",
  deactivated: "vouchers.error.gapDeactivated",
  worker: "vouchers.error.gapWorker",
  order: "vouchers.error.gapOrder",
};

const VOUCHER_REFUSALS: Record<string, CodedSentence> = {
  vouchers_not_deletable: (error, t) => refusedVoucher(error, t),

  voucher_archive_partial: (error, t) => {
    const archived = error.data?.archivedCount;
    const kept = error.data?.keptCount;
    if (archived === undefined || kept === undefined) return undefined;
    return t("vouchers.error.archivePartial", archived, kept);
  },

  // The button exists in the API but not in the panel; the sentence is here so the day it does, it
  // is already speaking the operator's language.
  manual_assign_disabled: (_error, t) => t("vouchers.error.assignDisabled"),
};

/** "None of the 3 ... V-123 — it has already been used": names the one voucher that stopped it. */
function refusedVoucher(error: ApiError, t: Translator): string | undefined {
  const number = text(error.extra?.voucherNumber);
  const gapKey = GAP_KEYS[text(error.extra?.gap) ?? ""];
  const refused = error.data?.refusedCount;
  const total = error.data?.totalCount;

  if (!number || !gapKey || refused === undefined || total === undefined)
    return undefined;

  return t("vouchers.error.notDeletable", refused, total, number, t(gapKey));
}

function text(value: unknown): string | undefined {
  return typeof value === "string" && value.trim() !== "" ? value : undefined;
}
