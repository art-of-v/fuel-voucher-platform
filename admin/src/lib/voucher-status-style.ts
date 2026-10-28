/**
 * The badge classes for a voucher's state, shared by the two tables that print raw vouchers.
 *
 * <p>
 * The two spent states live on their own axis and differ only in tense: a claim the customer can
 * still withdraw, and a sale the brand confirmed. They are the same two colours the mobile wallet
 * paints its "used" stamp with, because an operator on a support call reads the same voucher the
 * customer is holding. Every other state stays on the theme's status tokens.
 */

export const VOUCHER_MARKED_BADGE =
  "bg-voucher-marked/10 text-voucher-marked border-voucher-marked/20";

export const VOUCHER_CONFIRMED_BADGE =
  "bg-voucher-confirmed/10 text-voucher-confirmed border-voucher-confirmed/20";

/** `statusKey` is camelCase, as produced by `voucherStatusKey`. */
export function voucherStatusBadgeClass(statusKey: string): string {
  switch (statusKey) {
    case "available":
      return "bg-success/10 text-success border-success/20";
    case "assigned":
      return "bg-info/10 text-info border-info/20";
    case "used":
      return VOUCHER_CONFIRMED_BADGE;
    case "pendingReconciliation":
      return VOUCHER_MARKED_BADGE;
    default:
      return "bg-warning/10 text-warning border-warning/20";
  }
}
